// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 lnkiai

using System.IO.Pipes;
using System.Text;

namespace Meltype.Tests;

/// <summary>
/// 調査用: Meltype IME (TSF) のサーバーを、学習データを保存しない設定で単独で動かす / パイプにキーを送ってみる。
///   Meltype.Tests --tip-server [秒]     … サーバーだけを動かす (DLL を試すとき。Meltype.exe は止めておく)
///   Meltype.Tests --tip-client kyouha   … サーバーを動かし、打鍵を送って応答を出す
/// </summary>
internal static class TipHarness
{
    public static int Server(int seconds)
    {
        Application.EnableVisualStyles();
        // サーバーのログ (パイプを作れなかったときなど) を見られるように
        var log = Path.Combine(Path.GetTempPath(), "meltype-tip-server.log");
        Diagnostics.Log.SetFileOutput(log);
        Console.WriteLine($"ログ: {log}");
        var settings = new Config.Settings { Mode = Config.InputMode.Tsf };
        using var invoker = new Control();
        invoker.CreateControl();
        using var service = CreateService(invoker);
        using var server = new Tip.TipServer(invoker, service, () => settings);
        server.Start();
        Console.WriteLine($"\\\\.\\pipe\\{Tip.TipServer.PipeName} で待っています ({seconds} 秒)");
        using var timer = new System.Windows.Forms.Timer { Interval = seconds * 1000 };
        timer.Tick += (_, _) => Application.ExitThread();
        timer.Start();
        Application.Run();
        return 0;
    }

    public static int Client(string keys)
    {
        Application.EnableVisualStyles();
        var settings = new Config.Settings { Mode = Config.InputMode.Tsf, LiveConversion = false };
        using var invoker = new Control();
        invoker.CreateControl();
        using var service = CreateService(invoker);
        using var server = new Tip.TipServer(invoker, service, () => settings);
        server.Start();
        var failed = 0;
        var thread = new Thread(() =>
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", Tip.TipServer.PipeName, PipeDirection.InOut);
                pipe.Connect(3000);
                pipe.ReadMode = PipeTransmissionMode.Message;
                Console.WriteLine(Send(pipe, "{\"op\":\"hello\",\"process\":\"harness\"}"));
                foreach (var c in keys)
                {
                    // ' ' = Space、'\n' (\n と書く) = Enter
                    var vk = c switch { ' ' => 0x20, '\n' => 0x0D, >= 'a' and <= 'z' => c - 'a' + 'A', _ => (int)c };
                    var ch = c == '\n' ? 13 : c;
                    Console.WriteLine($"[{(c == '\n' ? "Enter" : c == ' ' ? "Space" : c.ToString())}] " +
                        Send(pipe, $"{{\"op\":\"key\",\"sid\":\"t1\",\"vk\":{vk},\"ch\":{ch},\"mods\":0,\"process\":\"harness\"}}"));
                }
                Console.WriteLine("[commit] " + Send(pipe, "{\"op\":\"commit\",\"sid\":\"t1\"}"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"失敗: {ex}");
                failed = 1;
            }
            finally
            {
                invoker.BeginInvoke(Application.ExitThread);
            }
        });
        thread.Start();
        Application.Run();
        return failed;
    }

    internal static Composition.CompositionService CreateService(Control invoker) =>
        new(invoker, Composition.CompositionDetector.CreateDefault(), new Composition.CompositionOptions
        {
            UserDictionary = new Composition.UserDictionary(null),
            History = new Composition.ConversionHistory(null),
            Languages = new Composition.LanguageMemory(null),
            TranslationHistory = new Composition.TranslationHistory(null),
            Engine = () => Config.ConversionEngine.System,
        });

    private static string Send(NamedPipeClientStream pipe, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        pipe.Write(bytes);
        var buffer = new byte[65536];
        using var message = new MemoryStream();
        do
        {
            var read = pipe.Read(buffer);
            if (read == 0) break;
            message.Write(buffer, 0, read);
        }
        while (!pipe.IsMessageComplete);
        return Encoding.UTF8.GetString(message.ToArray());
    }
}
