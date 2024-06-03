using System;
using System.Collections.Generic;
using OpenAI;
using OpenAI.Managers;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels.ResponseModels;


class OpenAIKeyNotFoundException : KeyNotFoundException
{
    public OpenAIKeyNotFoundException(string msg) : base(msg) { }
}

// OpenAIのサービスに接続できないよ系
class OpenAIServiceNotFoundException : Exception
{
    public OpenAIServiceNotFoundException(string msg) : base(msg) { }
}

internal partial class HmOpenAiGpt
{
    static ChatSession chatSession;
    static string model = Models.Gpt_3_5_Turbo;

    static int iMaxTokens = 4000;

    const string OpenAIKeyEnvironmentVariableName = "OPENAI_KEY";
    static string openai_key = null; // 直接APIの値を上書き指定している場合(マクロなどからの直接の引き渡し)
    const string ErrorMessageNoOpenAIKey = OpenAIKeyEnvironmentVariableName + "キーが環境変数にありません。\r\n";

    static string GetOpenAIKey()
    {
        if (String.IsNullOrEmpty(openai_key))
        {
            string key = Environment.GetEnvironmentVariable(OpenAIKeyEnvironmentVariableName);
            if (String.IsNullOrEmpty(key))
            {
                throw new OpenAIKeyNotFoundException(ErrorMessageNoOpenAIKey);
            }
            return key;
        }
        else
        {
            return openai_key;
        }
    }


    static void GenerateContent()
    {
        try
        {
            // main以外の場所でコマンドライン引数を取得する
            string[] commandLineArgs = Environment.GetCommandLineArgs();
            if (commandLineArgs.Length >= 4)
            {
                // Console.WriteLine("_projectId:" + commandLineArgs[1]);
                openai_key = commandLineArgs[1];
                // Console.WriteLine("_location:" + commandLineArgs[2]);
                model = commandLineArgs[2];

                // Console.WriteLine("_model:" + commandLineArgs[3]);
                iMaxTokens = int.Parse(commandLineArgs[3]);
            }
        }
        catch (Exception e)
        {
        }

        openai_key = GetOpenAIKey();

        System.Diagnostics.Trace.WriteLine("場所2");
        ClearAnswerFile();

        // コンテキストを追跡するためにチャットセッションを作成する
        chatSession = new ChatSession(openai_key, model, iMaxTokens);

        /*
        string prompt = "こんにちわ。私は日本語で会話します。";
        Console.WriteLine($"\nUser: {prompt}");

        string response = await chatSession.SendMessageAsync(prompt);
        Console.WriteLine($"Response: {response}");
        */

        /*
        prompt = "それを2倍すると？";
        Console.WriteLine($"\nUser: {prompt}");

        response = await chatSession.SendMessageAsync(prompt);
        Console.WriteLine($"Response: {response}");
        */
    }

}
