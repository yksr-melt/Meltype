// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 yksr-melt

using System;
using System.Runtime.InteropServices;

namespace Meltype.Signing;

// 信頼ストアを変えずに PE の Authenticode ハッシュを検証する。
// これだけでは信頼や署名者を保証しない。CMS の暗号署名・証明書の照合は呼び出し側で別に行う。
public static class Authenticode
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FileInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.LPWStr)] public string Path;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct TrustData
    {
        public uint Size;
        public IntPtr PolicyCallback;
        public IntPtr SipClient;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    public static uint VerifyFileHash(string path)
    {
        var file = new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        Marshal.StructureToPtr(file, pointer, false);
        var action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
        var data = new TrustData
        {
            Size = (uint)Marshal.SizeOf<TrustData>(),
            UiChoice = 2, // WTD_UI_NONE
            UnionChoice = 1, // WTD_CHOICE_FILE
            File = pointer,
            StateAction = 1, // WTD_STATEACTION_VERIFY
            // WTD_HASH_ONLY_FLAG | WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_DISABLE_MD2_MD4 | WTD_REVOCATION_CHECK_NONE
            ProviderFlags = 0x200 | 0x1000 | 0x2000 | 0x10,
        };
        try
        {
            return WinVerifyTrust(new IntPtr(-1), ref action, ref data);
        }
        finally
        {
            data.StateAction = 2; // WTD_STATEACTION_CLOSE は検証の成功・失敗によらず呼ぶ。
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            Marshal.DestroyStructure<FileInfo>(pointer);
            Marshal.FreeHGlobal(pointer);
        }
    }
}
