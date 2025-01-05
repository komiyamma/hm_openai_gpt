
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.RegularExpressions;


internal partial class ChatSession
{
    static string model = "gpt-4o-mini";

    static int iMaxTokens = 4000;

    static string strOpenAiKey = null; // 直接APIの値を上書き指定している場合(マクロなどからの直接の引き渡し)

    const string NewLine = "\r\n";
    const string ErrorMessageNoOpenAIKey = "OpenAI APIのキーが有効ではありません。:" + NewLine;



    // OpenAIにわたす会話ログ。基本的にOpenAIは会話の文脈を覚えているので、メッセージログ的なものを渡す必要がある。
    static List<ChatMessage> messageList = new();

    static object lockContents = new object();


    static int conversationUpdateCount = 1;
    public ChatSession(string openai_key, string _model, int maxtokens)
    {
        strOpenAiKey = openai_key;
        model = _model;
        iMaxTokens = maxtokens;

        InitMessages();
    }


    // 最初のシステムメッセージ。
    const string ChatGPTStartSystemMessage = "こんにちわ。"; // You are a helpful assistant. 日本語いれておくことで日本ユーザーをデフォルトとして考える

    public static void InitMessages()
    {
        List<ChatMessage> list = new List<ChatMessage>
        {
            new SystemChatMessage(ChatGPTStartSystemMessage)
        };
        lock (lockContents)
        {
            messageList = list;
        }

        InitMessageListRemoverTask();
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

    static DateTime lastCheckTime = DateTime.MinValue; // 1分前の時間からのスタート
    private void CancelCheck()
    {
        try
        {
            // 質問ファイルの日時調べる
            FileInfo fileInfo = new FileInfo(HmOpenAiGpt.questionFilePath);
            // ファイルが更新されていたら、チェック継続
            if (fileInfo.LastWriteTime > lastCheckTime)
            {
                lastCheckTime = fileInfo.LastWriteTime;
            }
            else
            {
                return;
            }

            string question_text = "";

            using (StreamReader reader = new StreamReader(HmOpenAiGpt.questionFilePath, Encoding.UTF8))
            {
                question_text = reader.ReadToEnd();
            }

            // 1行目にコマンドと質問がされた時刻に相当するTickCount相当の値が入っている
            // これによって値が進んでいることがわかる。
            // 正規表現を使用して数値を抽出
            Regex regex = new Regex(@"HmOpenAiGpt\.Cancel");
            Match match = regex.Match(question_text);
            if (match.Success)
            {
                this.Cancel();
                conversationUpdateCancel = true;
            }
        } catch (Exception)
        {
        }
    }

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
        return strOpenAiKey;
    }

    // OpenAIサービスのインスタンス。一応保持
    static ChatClient openAiService = null;

    static ChatClient ConnectOpenAIService(string key)
    {
        try
        {
            var openAiService = new ChatClient(model, key);
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
    static AsyncCollectionResult<StreamingChatCompletionUpdate> ReBuildPastChatContents(CancellationToken ct)
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

        // 元々ChatGPTの方でも4000トークンぐらいでセーフティがかかってる模様
        ChatCompletionOptions options = null;

        options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = iMaxTokens
        };

        lock (lockContents)
        {
            // ストリームとして会話モードを確率する。ストリームにすると解答が１文字ずつ順次表示される。
            var completionResult = openAiService.CompleteChatStreamingAsync(messageList, options, ct);
            // エラー内容をファイルに出力
            return completionResult;
        }
    }


    public void AddQuestion(string question)
    {
        lock (lockContents)
        {
            messageList.Add(new UserChatMessage(question));
        }
    }

    private static void AddAnswer(string answer_sum)
    {
        lock (lockContents)
        {
            // 今回の返答ををChatGPTの返答として記録しておく
            messageList.Add(new AssistantChatMessage(answer_sum));
        }
    }

    // 最後の「質問と応答」の履歴を削除
    public void PopCotent()
    {
        lock (lockContents)
        {
            var len = messageList.Count;
            // 1番目はシステム。最後の２つを除去する。
            if (len >= 3)
            {
                messageList.RemoveRange(len-2, 2);
            }
        }
    }


    const string ErrorMsgUnknown = "Unknown Error:" + NewLine;
    // チャットの反復

    const string AssistanceAnswerCancelMsg = "AIの応答をキャンセルしました。";

    // 最低、何文字以上変化があったら一端出力するか
    const int flushOfStringLengthChange = 40;

    public async Task SendMessageAsync(string prompt, int questionNumber)
    {
        try
        {
            var task = conversationUpdateCheck();

            _cst = new CancellationTokenSource();
            var ct = _cst.Token;

            string answer_sum = "";

            AddQuestion(prompt);
            var completionResult = ReBuildPastChatContents(ct);

            int flushedLength = 0;

            bool isMustCancelCheck = false;

            // ストリーム型で確立しているので、async的に扱っていく
            await foreach (var completion in completionResult)
            {
                // 途中で分詰まりを検知するための進捗カウンタ
                conversationUpdateCount++;

                // 毎回じゃ重いので、適当に間引く
                if (isMustCancelCheck)
                {
                    CancelCheck();
                    isMustCancelCheck = false;
                    // System.Diagnostics.Trace.WriteLine("CancelCheck");
                }

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
                if (completion.ContentUpdate.Count > 0)
                {
                    // ちろっと文字列追加表示
                    string str = completion.ContentUpdate[0].Text;
                    if (str != null)
                    {
                        answer_sum += str ?? "";
                        var currentLength = answer_sum.Length;
                        if (currentLength > flushedLength + flushOfStringLengthChange)
                        {
                            flushedLength = currentLength;
                            SaveAllTextToFile(answer_sum);
                            isMustCancelCheck = true;
                        }
                    }
                }
                else
                {
                    /*
                    answer_sum += completion.FinishReason.ToString();

                    // 失敗なら何かエラーと原因を表示
                    if (completion.FinishReason != ChatFinishReason.Stop)
                    {
                        throw new Exception(completion.FinishReason.ToString());
                    }
                    */

                }
            }
            // Console.WriteLine(answer_sum);
            AddAnswer(answer_sum);
            // 最後に念のために、全体のテキストとして1回上書き保存しておく。
            // 細かく保存していた際に、ファイルIOで欠損がある可能性がわずかにあるため。
            SaveAllTextToFile(answer_sum);
            SaveCompleteFile(questionNumber);
            // 解答が完了したよ～というのを人にわかるように表示
            // output.WriteLine(AssistanceAnswerCompleteMsg);
        }
        catch (Exception e)
        {
            conversationUpdateCancel = true;
            this.Cancel();
            // Console.WriteLine("問い合わせをキャンセルしました。" + e);
            // Console.WriteLine("アプリを終了します。");
            SaveAddTextToFile("\r\n\r\n" + e.GetType().Name + "\r\n\r\n" + e.Message + "\r\n");
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

    private void SaveAllTextToFile(string text)
    {
        HmOpenAiGpt.SaveAllTextToAnswerFile(text);
    }

    private void SaveCompleteFile(int number)
    {
        HmOpenAiGpt.SaveCompleteFile(number);
    }

}
