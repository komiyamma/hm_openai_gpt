using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

internal partial class ChatSession
{
    // 最後の回答があってから5分以上経過したらメッセージリストの一番最初のリストを削除する
    static private DateTime lastAnswerTime = DateTime.Now;
    static private DateTime lastDeleteTime = DateTime.MinValue;

    static CancellationTokenSource autoRemoverCancelTokenSource;

    static Task taskRunMessageListRemove;
    public static void InitMessageListRemoverTask()
    {
        // System.Diagnostics.Trace.WriteLine("InitMessageListCancelToken\r\n");
        lastAnswerTime = DateTime.Now;
        lastDeleteTime = DateTime.Now;

        // 初めてのタスク発行、もしくはタスクが完了してしまっていたら、改めて発行する
        if (taskRunMessageListRemove == null || taskRunMessageListRemove.IsCompleted)
        {
            autoRemoverCancelTokenSource = new CancellationTokenSource();

            taskRunMessageListRemove = Task.Run(async () =>
            {
                await RunMessageListRemoveTask();
            });
        }
    }

    public static async Task RunMessageListRemoveTask()
    {
        while (true)
        {
            // System.Diagnostics.Trace.WriteLine("RunMessageListRemoveTask");
            if (autoRemoverCancelTokenSource != null && autoRemoverCancelTokenSource.IsCancellationRequested)
            {
                return;
            }

            var lastCondition = (DateTime.Now - lastAnswerTime).TotalMinutes >= 10; // 10分
            var tickConsition = (DateTime.Now - lastDeleteTime).TotalMinutes >= 1;  // 1分

            // ５分以上経過していて、1分チックも達成している。
            if (lastCondition && tickConsition)
            {
                RemoveEarliestQandA();
            }
            // 最後の回答があってから10分間以上経過している
            else if (lastCondition)
            {
                RemoveEarliestQandA();
            }

            if (autoRemoverCancelTokenSource != null)
            {
                var token = autoRemoverCancelTokenSource.Token;
                await Task.Delay(TimeSpan.FromMinutes(1), token);
            }
        }
    }

    public static void RemoveEarliestQandA()
    {
        lock (lockContents)
        {
            // 先頭の一つはシステムメッセージなので、index[1]とindex[2]を削除する。
            if (messageList.Count >= 3)
            {
                messageList.RemoveRange(1, 2);
                // System.Diagnostics.Trace.WriteLine("RemoveEarliestQandA");
            }
            lastDeleteTime = DateTime.Now;

        }
    }

}
