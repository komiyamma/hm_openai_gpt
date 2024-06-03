
using OpenAI;
using OpenAI.Managers;
using OpenAI.ObjectModels;
using OpenAI.ObjectModels.RequestModels;
using OpenAI.ObjectModels.ResponseModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


internal class ChatSession
{
    static string model = Models.Gpt_3_5_Turbo;

    static int iMaxTokens = 4000;

    static string OpenAIKeyOverWriteVariable = null; // 直接APIの値を上書き指定している場合(マクロなどからの直接の引き渡し)

    const string NewLine = "\r\n";
    const string ErrorMessageNoOpenAIKey = "OpenAI APIのキーが有効ではありません。:" + NewLine;



    // OpenAIにわたす会話ログ。基本的にOpenAIは会話の文脈を覚えているので、メッセージログ的なものを渡す必要がある。
    static List<ChatMessage> messageList = new();


    static int conversationUpdateCount = 1;
    public ChatSession(string openai_key, string _model, int maxtokens)
    {
        OpenAIKeyOverWriteVariable = openai_key;
        model = _model;
        iMaxTokens = maxtokens;

        InitMessages();
    }


    // 最初のシステムメッセージ。
    const string ChatGPTStartSystemMessage = "何かお手伝い出来ることはありますか？"; // You are a helpful assistant. 日本語いれておくことで日本ユーザーをデフォルトとして考える

    public static void InitMessages()
    {
        List<ChatMessage> list = new List<ChatMessage>
        {
            ChatMessage.FromSystem(ChatGPTStartSystemMessage)
        };
        messageList = list;
    }



    // AIからの返答がどうも進んでいない、といったことを判定する。５秒進んでいないようだと、キャンセルを発動する。
    bool conversationUpdateCancel = false;
    async Task conversationUpdateCheck()
    {
        conversationUpdateCancel = false;
        int lastConversationUpdateCount = conversationUpdateCount;
        long iTickCount = 0;
        while (true)
        {
            if (conversationUpdateCancel)
            {
                // Console.WriteLine("今回の会話タスクが終了したため、conversationUpdateCheckを終了");
                break;
            }
            await Task.Delay(100); // 5秒ごとにチェック

            if (lastConversationUpdateCount == conversationUpdateCount)
            {
                iTickCount++;
            }
            else
            {
                lastConversationUpdateCount = conversationUpdateCount;
                iTickCount = 0;
            }

            if (iTickCount > 50)
            {
                iTickCount = 0;
                // Console.WriteLine("AIからの応答の進捗がみられないため、キャンセル発行");
                this.Cancel();
                break;
            }
        }
    }

    // 質問してAIの応答の途中でキャンセルするためのトークン
    static CancellationTokenSource _cst;

    // 会話履歴全部クリア
    public void Clear()
    {
        InitMessages();
    }

    // AIの返答を途中キャンセル
    public void Cancel()
    {
        _cst.Cancel();
    }

    const string ErrorMessageNoOpenAIService = "OpenAIのサービスに接続できません。:" + NewLine;

    static string GetOpenAIKey()
    {
        return OpenAIKeyOverWriteVariable;
    }

    // OpenAIサービスのインスタンス。一応保持
    static OpenAIService openAiService = null;

    static OpenAIService ConnectOpenAIService(string key)
    {
        try
        {
            var openAiService = new OpenAIService(new OpenAiOptions()
            {
                ApiKey = key
            });

            return openAiService;
        }
        catch (Exception)
        {
            openAiService = null;
            InitMessages();
            throw;
        }
    }

    // チャットのエンジンやオプション。過去のチャット内容なども渡す。
    static IAsyncEnumerable<ChatCompletionCreateResponse> ReBuildPastChatContents(CancellationToken ct)
    {
        var key = GetOpenAIKey();
        if (key == null)
        {
            throw new OpenAIKeyNotFoundException(ErrorMessageNoOpenAIKey);
        }

        List<ChatMessage> list = new();
        if (openAiService == null)
        {
            openAiService = ConnectOpenAIService(key);
        }
        if (openAiService == null)
        {
            throw new OpenAIServiceNotFoundException(ErrorMessageNoOpenAIService);
        }

        // オプション。1000～2000トークンぐらいでセーフティかけておくのがいいだろう。
        // 元々ChatGPTの方でも4000トークンぐらいでセーフティがかかってる模様
        var options = new ChatCompletionCreateRequest
        {
            Messages = messageList,
            Model = model,
            MaxTokens = iMaxTokens
        };

        // ストリームとして会話モードを確率する。ストリームにすると解答が１文字ずつ順次表示される。
        var completionResult = openAiService.ChatCompletion.CreateCompletionAsStream(options, null, false, ct);
        return completionResult;
    }


    public void AddQuestion(string question)
    {
        messageList.Add(ChatMessage.FromUser(question));
    }

    private static void AddAnswer(string answer_sum)
    {
        // 今回の返答ををChatGPTの返答として記録しておく
        messageList.Add(ChatMessage.FromAssistant(answer_sum));
    }

    const string AssistanceAnswerCompleteMsg = NewLine + "-- 完了 --" + NewLine;
    const string ErrorMsgUnknown = "Unknown Error:" + NewLine;
    // チャットの反復

    const string AssistanceAnswerCancelMsg = NewLine + "-- ChatGPTの回答を途中キャンセルしました --" + NewLine;
    public string GetAssistanceAnswerCancelMsg()
    {
        return AssistanceAnswerCancelMsg;
    }


    public async Task SendMessageAsync(string prompt)
    {
        try
        {

            var task = conversationUpdateCheck();
            _cst = new CancellationTokenSource();
            var ct = _cst.Token;

            string answer_sum = "";
            var completionResult = ReBuildPastChatContents(ct);

            // ストリーム型で確立しているので、async的に扱っていく
            await foreach (var completion in completionResult)
            {
                // 途中で分詰まりを検知するための進捗カウンタ
                conversationUpdateCount++;

                // キャンセルが要求された時、
                if (ct.IsCancellationRequested)
                {
                    // 一応Dispose呼んでおく(CancellationToken渡しているので不要なきもするが...)
                    await completionResult.GetAsyncEnumerator().DisposeAsync();
                    throw new OperationCanceledException(AssistanceAnswerCancelMsg);
                }

                // キャンセルされてたら OperationCanceledException を投げるメソッド
                ct.ThrowIfCancellationRequested();

                // 会話成功なら
                if (completion.Successful)
                {
                    // ちろっと文字列追加表示
                    string str = completion.Choices.FirstOrDefault()?.Message.Content;
                    if (str != null)
                    {
                        // SaveAddTextToFile(str);
                        answer_sum += str ?? "";
                    }
                }
                else
                {
                    // 失敗なら何かエラーと原因を表示
                    if (completion.Error == null)
                    {
                        throw new Exception(ErrorMsgUnknown);
                    }

                    SaveAddTextToFile($"{completion.Error.Code}: {completion.Error.Message}" + "\n");
                }
            }
            Console.WriteLine(answer_sum);
            AddAnswer(answer_sum);

            // 解答が完了したよ～というのを人にわかるように表示
            // output.WriteLine(AssistanceAnswerCompleteMsg);
        }
        catch (Exception e)
        {
            SaveAddTextToFile("\n\n\n" + e.GetType().Name + "\r\n" + e.Message + "\n\n\n");
            conversationUpdateCancel = true;
            this.Cancel();
            // Console.WriteLine("問い合わせをキャンセルしました。" + e);
            // Console.WriteLine("アプリを終了します。");
            Environment.Exit(0);
        }
        finally
        {
            conversationUpdateCancel = true;
        }
    }


    // Streamでちょこちょこと返答が返ってくるので、ちょこちょこと返答内容をファイルに追加保存する。
    private void SaveAddTextToFile(string text)
    {
        HmOpenAiGpt.SaveAddTextToAnswerFile(text);
    }

}
