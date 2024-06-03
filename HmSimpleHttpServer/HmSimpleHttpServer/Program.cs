/*
 * Copyright (c) 2024 Akitsugu Komiyama
 * under the MIT License
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;


internal class HmSimpleHttpServer
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool IsWindow(nint hWnd);

    // 秀丸の該当プロセスのウィンドウハンドルの値がもらいやすいので、これが存在しなくなっていたら、このプロセスも終了するようにする。
    static async Task Main(String[] args)
    {

        nint hmWndHandle = 0;
        try
        {
            if (args.Length > 0)
            {
                hmWndHandle = (nint)long.Parse(args[0]);
            }
        }
        catch (Exception) { }

        HmSimpleHttpServer server = new HmSimpleHttpServer();
        int port = server.Launch();
        Console.WriteLine("PORT:" + port);

        while (true)
        {
            await Task.Delay(2000); // 2秒待つ
            if (!IsWindow(hmWndHandle))
            {
                break;
            }
            if (phpProcess == null)
            {
                break;
            }
            if (phpProcess.HasExited)
            {
                break;
            }
        }

        server.Destroy();
        Console.WriteLine("秀丸ウィンドウハンドル:" + hmWndHandle + "から呼ばれたHmSimpleHttpServerはクローズします。");
        // 何か外部からインプットがあれば終了し、このserverインスタンスが終われば、対応したphpサーバープロセスもkillされる。
    }

    static Process phpProcess;

    // PHPデーモンのスタート
    HmSimpleHttpServer()
    {
        try
        {
            Destroy();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString() + "\r\n");
        }
    }
    ~HmSimpleHttpServer()
    {
        Destroy();
    }

    public int Launch()
    {
        return CreatePHPServerProcess();
    }

    static List<int> portsInUse;
    public static int AvailablePort()
    {
        var ipGP = IPGlobalProperties.GetIPGlobalProperties();
        var tcpEPs = ipGP.GetActiveTcpListeners();
        var udpEPs = ipGP.GetActiveUdpListeners();
        portsInUse = tcpEPs.Concat(udpEPs).Select(p => p.Port).ToList();

        for (int port = 41000; port <= 60000; ++port)
        {
            if (!portsInUse.Contains(port))
            {
                return port;
            }
        }

        return 0; // 空きポートが見つからない場合
    }

    // PHPプロセス生成
    private int CreatePHPServerProcess()
    {
        try
        {
            string phpServerDocumentFolder = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string phpExePath = Path.Combine(phpServerDocumentFolder, "php.exe");
            int port = AvailablePort();
            string phpHostName = "localhost";

            phpProcess = new Process();
            ProcessStartInfo psi = phpProcess.StartInfo;
            psi.FileName = phpExePath;
            psi.Arguments = $" -S {phpHostName}:{port} -t \"{phpServerDocumentFolder}\" ";

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = false;
            psi.RedirectStandardError = false;
            psi.WindowStyle = ProcessWindowStyle.Hidden;

            phpProcess.Start();
            return port;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString() + "\r\n");
        }
        return 0;
    }

    private int Destroy()
    {
        try
        {
            if (phpProcess != null)
            {
                phpProcess.Kill();
            }

            return 1;
        }
        catch (Exception)
        {

        }

        return 0;
    }
}
