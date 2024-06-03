/*
 * Copyright (c) 2023 Akitsugu Komiyama
 * under the MIT License
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;


internal class HmOpenAiGptHttpServer
{
    static void Main()
    {
        HmOpenAiGptHttpServer server = new HmOpenAiGptHttpServer();
        int port = server.Launch();
        Console.WriteLine(port);

        // ここで止めておく。
        Console.In.ReadLine();
        server.Destroy();
        // 何か外部からインプットがあれば終了し、このserverインスタンスが終われば、対応したphpサーバープロセスもkillされる。
    }

    static Process phpProcess;

    // PHPデーモンのスタート
    HmOpenAiGptHttpServer()
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
    ~HmOpenAiGptHttpServer()
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
