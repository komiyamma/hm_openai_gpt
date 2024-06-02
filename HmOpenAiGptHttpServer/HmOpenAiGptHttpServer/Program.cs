/*
 * Copyright (c) 2023 Akitsugu Komiyama
 * under the MIT License
 */

using HmNetCOM;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;



namespace HmOpenAiGptHttpServer
{

    public interface IHmOpenAiGptHttpServer // 外部から利用できるメソッドに対してinterfaceの定義が必須になる。
    {
        int Launch();
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)] // これは必須
    [Guid("8F3C1091-7DC8-4921-A49E-408EA5E51610")]
    public class HmOpenAiGptHttpServer : IHmOpenAiGptHttpServer
    {
        static Process phpProcess;

        // PHPデーモンのスタート
        public int Launch()
        {
            try
            {
                Destroy();

                return CreatePHPServerProcess();
            }
            catch (Exception e)
            {
                Hm.OutputPane.Output(e.ToString() + "\r\n");
            }

            return 0;

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
                Hm.OutputPane.Output(ex.ToString() + "\r\n");
            }
            return 0;
        }


        private static async Task<CancellationToken> DelayMethod(CancellationToken ct)
        {
            await Task.Delay(150);
            if (ct.IsCancellationRequested)
            {
                // Clean up here, then...
                ct.ThrowIfCancellationRequested();
            }

            return ct;
        }

        public void OnReleaseObject(int reason = 0)
        {
            Destroy();
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
}
