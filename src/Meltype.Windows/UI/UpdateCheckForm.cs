// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 aster-void

namespace Meltype.UI;

/// <summary>
/// 「今すぐ更新を確認」で、確認とダウンロードが終わるまで出す窓 (新しい版があると、ダウンロードに数分かかる)。
/// 閉じても確認は止めない (Updater に止める方法が無い)。終わったら結果を別に知らせる。
/// </summary>
internal sealed class UpdateCheckForm : Form
{
    public UpdateCheckForm()
    {
        Text = "Meltype の更新";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Yu Gothic UI", 9.5F);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Padding = new Padding(16) };
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(360), 0),
            Text = "新しい版を確認しています。新しい版があれば、ダウンロードしてから更新するかを聞きます (数分かかることがあります)。",
        });
        layout.Controls.Add(new ProgressBar { Style = ProgressBarStyle.Marquee, Width = LogicalToDeviceUnits(360), Margin = new Padding(0, 12, 0, 0) });
        Controls.Add(layout);
    }
}
