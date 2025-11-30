using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;


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
        //Trace.WriteLine($"[ChatSession.ctor] keyLen={openai_key?.Length ?? 0}, model={_model}, maxTokens={maxtokens}");
        strOpenAiKey = openai_key;
        model = _model;
        iMaxTokens = maxtokens;

        InitMessages();
    }


    // 最初のシステムメッセージ。
    const string ChatGPTStartSystemMessage = "こんにちわ。"; // You are a helpful assistant. 日本語いれておくことで日本ユーザーをデフォルトとして考える

    public static void InitMessages()
    {
        //Trace.WriteLine("[InitMessages] 初期化開始");
        List<ChatMessage> list = new List<ChatMessage>
        {
            new AssistantChatMessage(ChatGPTStartSystemMessage)
        };
        lock (lockContents)
        {
            messageList = list;
            //Trace.WriteLine($"[InitMessages] messageList.Count={messageList.Count}");
        }

        //Trace.WriteLine("[InitMessages] InitMessageListRemoverTask 呼び出し");
        InitMessageListRemoverTask();
    }



    // AIからの返答がどうも進んでいない、といったことを判定する。10秒進んでいないようだと、キャンセルを発動する。
    bool conversationUpdateCancel = false;

    async Task conversationUpdateCheck()
    {
        //Trace.WriteLine("[conversationUpdateCheck] 監視開始");
        conversationUpdateCancel = false;
        int lastConversationUpdateCount = conversationUpdateCount;
        long iTickCount = 0;
        while (true)
        {
            if (conversationUpdateCancel)
            {
                //Trace.WriteLine("[conversationUpdateCheck] 終了フラグ検知");
                break;
            }
            await Task.Delay(100); // 10秒ごとにチェック

            if (lastConversationUpdateCount == conversationUpdateCount)
            {
                iTickCount++;
            }
            else
            {
                //Trace.WriteLine($"[conversationUpdateCheck] 進捗: {lastConversationUpdateCount} -> {conversationUpdateCount}");
                lastConversationUpdateCount = conversationUpdateCount;
                iTickCount = 0;
            }

            if (iTickCount > 100)
            {
                iTickCount = 0;
                //Trace.WriteLine("[conversationUpdateCheck] 応答停滞 -> Cancel()");
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
            //Trace.WriteLine("[CancelCheck] 開始");
            // 質問ファイルの日時調べる
            FileInfo fileInfo = new FileInfo(HmOpenAiGpt.questionFilePath);
            //Trace.WriteLine($"[CancelCheck] path={HmOpenAiGpt.questionFilePath}, lastWrite={fileInfo.LastWriteTime}, lastCheck={lastCheckTime}");
            // ファイルが更新されていたら、チェック継続
            if (fileInfo.LastWriteTime > lastCheckTime)
            {
                lastCheckTime = fileInfo.LastWriteTime;
            }
            else
            {
                //Trace.WriteLine("[CancelCheck] 更新なし");
                return;
            }

            string question_text = "";

            using (StreamReader reader = new StreamReader(HmOpenAiGpt.questionFilePath, Encoding.UTF8))
            {
                question_text = reader.ReadToEnd();
            }
            //Trace.WriteLine($"[CancelCheck] readLen={question_text.Length}");

            // 1行目にコマンドと質問がされた時刻に相当するTickCount相当の値が入っている
            // これによって値が進んでいることがわかる。
            // 正規表現を使用して数値を抽出
            Regex regex = new Regex(@"HmOpenAiGpt\.Cancel");
            Match match = regex.Match(question_text);
            if (match.Success)
            {
                //Trace.WriteLine("[CancelCheck] Cancel 検知 -> Cancel()");
                this.Cancel();
                conversationUpdateCancel = true;
            }
            else
            {
                //Trace.WriteLine("[CancelCheck] Cancel なし");
            }
        } catch (Exception ex)
        {
            //Trace.WriteLine($"[CancelCheck] 例外: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // 会話履歴全部クリア
    public void Clear()
    {
        //Trace.WriteLine("[Clear] 会話履歴クリア");
        InitMessages();
    }

    // AIの返答を途中キャンセル
    public void Cancel()
    {
        //Trace.WriteLine("[Cancel] 要求");
        try
        {
            _cst.Cancel();
            //Trace.WriteLine("[Cancel] CancellationTokenSource.Cancel() 実行");
        }
        catch (Exception ex)
        {
            //Trace.WriteLine($"[Cancel] 例外: {ex.GetType().Name}: {ex.Message}");
        }
    }

    const string ErrorMessageNoOpenAIService = "OpenAIのサービスに接続できません。:" + NewLine;

    static string GetOpenAIKey()
    {
        //Trace.WriteLine("[GetOpenAIKey] キー取得");
        return strOpenAiKey;
    }

    // OpenAIサービスのインスタンス。一応保持
    static ChatClient openAiService = null;

    static ChatClient ConnectOpenAIService(string key)
    {
        //Trace.WriteLine($"[ConnectOpenAIService] 接続試行 model={model}, keyLen={key?.Length ?? 0}");
        try
        {
            var openAiService = new ChatClient(model, key);
            //Trace.WriteLine("[ConnectOpenAIService] 接続成功");
            return openAiService;
        }
        catch (Exception)
        {
            //Trace.WriteLine("[ConnectOpenAIService] 接続失敗 -> InitMessages & rethrow");
            openAiService = null;
            InitMessages();
            throw;
        }
    }

    // チャットのエンジンやオプション。過去のチャット内容なども渡す。
    static AsyncCollectionResult<StreamingChatCompletionUpdate> ReBuildPastChatContents(CancellationToken ct)
    {
        //Trace.WriteLine("[ReBuildPastChatContents] 構築開始");
        var key = GetOpenAIKey();
        if (key == null)
        {
            //Trace.WriteLine("[ReBuildPastChatContents] キーなし -> 例外");
            throw new OpenAIKeyNotFoundException(ErrorMessageNoOpenAIKey);
        }

        List<ChatMessage> list = new();
        if (openAiService == null)
        {
            //Trace.WriteLine("[ReBuildPastChatContents] openAiService==null -> 接続");
            openAiService = ConnectOpenAIService(key);
        }
        if (openAiService == null)
        {
            //Trace.WriteLine("[ReBuildPastChatContents] openAiService 構築失敗 -> 例外");
            throw new OpenAIServiceNotFoundException(ErrorMessageNoOpenAIService);
        }

        // 元々ChatGPTの方でも4000トークンぐらいでセーフティがかかってる模様
        ChatCompletionOptions options = null;

        // #pragma warning disable SCME0001 // Type is for evaluation purposes only and is subject to change or removal in future updates.

        // options.Patch.Set("$.reasoning_effort"u8, "minimal");

        options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = iMaxTokens,
        };


        lock (lockContents)
        {
            //Trace.WriteLine($"[ReBuildPastChatContents] messageList.Count={messageList.Count} -> ストリーミング開始");
            // ストリームとして会話モードを確率する。ストリームにすると解答が１文字ずつ順次表示される。
            var completionResult = openAiService.CompleteChatStreamingAsync(messageList, options, ct);
            // エラー内容をファイルに出力
            return completionResult;
        }
    }


    public void AddQuestion(string question)
    {
        //Trace.WriteLine($"[AddQuestion] len={question?.Length ?? 0}");
        lock (lockContents)
        {
            messageList.Add(new UserChatMessage(question));
            //Trace.WriteLine($"[AddQuestion] messageList.Count={messageList.Count}");
        }
    }

    private static void AddAnswer(string answer_sum)
    {
        //Trace.WriteLine($"[AddAnswer] len={answer_sum?.Length ?? 0}");
        lock (lockContents)
        {
            // 今回の返答ををChatGPTの返答として記録しておく
            messageList.Add(new AssistantChatMessage(answer_sum));
            //Trace.WriteLine($"[AddAnswer] messageList.Count={messageList.Count}");
        }
    }

    // 最後の「質問と応答」の履歴を削除
    public void PopCotent()
    {
        //Trace.WriteLine("[PopCotent] 実行");
        lock (lockContents)
        {
            var len = messageList.Count;
            // 1番目はシステム。最後の２つを除去する。
            if (len >= 3)
            {
                messageList.RemoveRange(len-2, 2);
                //Trace.WriteLine($"[PopCotent] 削除後件数={messageList.Count}");
            }
            else
            {
                //Trace.WriteLine("[PopCotent] 件数不足のためスキップ");
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
        //Trace.WriteLine($"[SendMessageAsync] 開始 questionNumber={questionNumber}, promptLen={prompt?.Length ?? 0}");
        try
        {
            var task = conversationUpdateCheck();

            _cst = new CancellationTokenSource();
            var ct = _cst.Token;
            //Trace.WriteLine("[SendMessageAsync] CancellationTokenSource 作成");

            string answer_sum = "";

            AddQuestion(prompt);
            var completionResult = ReBuildPastChatContents(ct);

            // デバッグでcompletionResultの中身を逐次表示


            int flushedLength = 0;

            bool isMustCancelCheck = false;

            // ストリーム型で確立しているので、async的に扱っていく
            await foreach (var completion in completionResult)
            {
                // 途中で分詰まりを検知するための進捗カウンタ
                conversationUpdateCount++;
                //Trace.WriteLine($"[SendMessageAsync] 受信 ContentUpdate.Count={completion.ContentUpdate.Count}, progress={conversationUpdateCount}");

                // 毎回じゃ重いので、適当に間引く
                if (isMustCancelCheck)
                {
                    //Trace.WriteLine("[SendMessageAsync] CancelCheck 実行");
                    CancelCheck();
                    isMustCancelCheck = false;
                    // System.Diagnostics.Trace.WriteLine("CancelCheck");
                }

                // キャンセルが要求された時、
                if (ct.IsCancellationRequested)
                {
                    // 一応Dispose呼んでおく(CancellationToken渡しているので不要なきもするが...)
                    //Trace.WriteLine("[SendMessageAsync] IsCancellationRequested -> Dispose & throw");
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
                    //Trace.WriteLine($"[SendMessageAsync] chunkLen={str?.Length ?? 0}");
                    if (str != null)
                    {
                        answer_sum += str ?? "";
                        var currentLength = answer_sum.Length;
                        if (currentLength > flushedLength + flushOfStringLengthChange)
                        {
                            flushedLength = currentLength;
                            //Trace.WriteLine($"[SendMessageAsync] flush 保存 currentLen={currentLength}");
                            SaveAllTextToFile(answer_sum);
                            isMustCancelCheck = true;
                        }
                    }
                }
                /*
                else if (completion.FinishReason == ChatFinishReason.Stop)
                {
                    answer_sum += completion.FinishReason.ToString();
                    throw new Exception(completion.FinishReason.ToString());
                }
                */
            }

            //Trace.WriteLine("[SendMessageAsync] ストリーム完了 -> AddAnswer/SaveAll/SaveComplete");
            AddAnswer(answer_sum);
            // 最後に念のために、全体のテキストとして1回上書き保存しておく。
            // 細かく保存していた際に、ファイルIOで欠損がある可能性がわずかにあるため。
            SaveAllTextToFile(answer_sum);
            SaveCompleteFile(questionNumber);
            // 解答が完了したよ～というのを人にわかるように表示
            // output.WriteLine(AssistanceAnswerCompleteMsg);
        }
        catch (OperationCanceledException oce)
        {
            //Trace.WriteLine($"[SendMessageAsync] OperationCanceledException: {oce.Message}");
            conversationUpdateCancel = true;
            // Console.WriteLine("問い合わせをキャンセルしました。" + e);
            // Console.WriteLine("アプリを終了します。");
            SaveAddTextToFile("\r\n\r\n" + oce.GetType().Name + "\r\n\r\n" + oce.Message + "\r\n");
            this.Cancel();
            Environment.Exit(0);
        }
        catch (Exception e)
        {
            //Trace.WriteLine($"[SendMessageAsync] 例外: {e.GetType().Name}: {e.Message}");
            conversationUpdateCancel = true;
            // Console.WriteLine("問い合わせをキャンセルしました。" + e);
            // Console.WriteLine("アプリを終了します。");
            SaveAddTextToFile("\r\n\r\n" + e.GetType().Name + "\r\n\r\n" + e.Message + "\r\n");
            this.Cancel();
            Environment.Exit(0);
        }
        finally
        {
            //Trace.WriteLine("[SendMessageAsync] finally 終了フラグ設定");
            conversationUpdateCancel = true;
        }
    }


    // Streamでちょこちょこと返答が返ってくるので、ちょこちょこと返答内容をファイルに追加保存する。
    private void SaveAddTextToFile(string text)
    {
        //Trace.WriteLine($"[SaveAddTextToFile] len={text?.Length ?? 0}");
        HmOpenAiGpt.SaveAddTextToAnswerFile(text);
    }

    private void SaveAllTextToFile(string text)
    {
        //Trace.WriteLine($"[SaveAllTextToFile] len={text?.Length ?? 0}");
        HmOpenAiGpt.SaveAllTextToAnswerFile(text);
    }

    private void SaveCompleteFile(int number)
    {
        //Trace.WriteLine($"[SaveCompleteFile] number={number}");
        HmOpenAiGpt.SaveCompleteFile(number);
    }

}
