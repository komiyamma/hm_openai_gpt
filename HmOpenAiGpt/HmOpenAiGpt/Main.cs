using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;



internal partial class HmOpenAiGpt
{
    static void ifProcessHasExistKillIt()
    {
        // 現在のプロセスの名前を取得
        string currentProcessName = Process.GetCurrentProcess().ProcessName;

        // 既に起動している同じプロセス名のプロセスを取得
        var runningProcesses = Process.GetProcessesByName(currentProcessName);

        // 起動しているプロセスが2つ以上ある場合は、
        if (runningProcesses.Length > 1)
        {
            // 新しいプロセス(今このプログラム行を実行しているプロセス = カレントプロセス)を終了させる
            Environment.Exit(0);
        }
    }

    static async Task Main(String[] args)
    {
        // 自分が2個目なら終了(2重起動しｊない)
        ifProcessHasExistKillIt();

        // クリアの命令をすると、先に実行していた方が先に閉じてしまうことがある。
        // よってマクロから明示的にClearする時は、引数にて「実行を継続するようなプロセスではないですよ」といった意味で
        // HmOpenAiGpt.Clear という文字列を渡してある
        if (args.Length >= 1)
        {
            var command = args[0];
            if (command.Contains("HmOpenAiGpt.Clear()"))
            {
                return;
            }
            if (command.Contains("HmOpenAiGpt.Cancel()"))
            {
                return;
            }
        }

        // Windowsがシャットダウンするときに呼び出される処理を登録等
        WindowsShutDownNotifier();

        // 会話エンジンを初期化
        GenerateContent();

        // ファイル監視を開始
        StartFileWatchr();

        await Task.Delay(-1); // 無期限で待機する
    }
}
