using static TdLib.TdApi;
using TdLib;
using TdLib.Bindings;

namespace TGApp
{
    internal static class Program
    {
        private const int ApiId = 19593366;
        private const string ApiHash = "09c1c085b8ed844ece9de511362eee4a";
        // PhoneNumber must contain international phone with (+) prefix.
        // For example +16171234567

        private const string PhoneNumber = "380684301782";
        private const string ApplicationVersion = "0.0.1";

        private static TdClient _client = new();
        private static readonly ManualResetEventSlim ReadyToAuthenticate = new();

        private static bool _authNeeded;
        private static bool _passwordNeeded;

        private static async Task Main()
        {
            // Creating Telegram client and setting minimal verbosity to Fatal since we don't need a lot of logs :)
            _client = new TdClient();

            _client.Bindings.SetLogVerbosityLevel(TdLogLevel.Fatal);

            // Subscribing to all events
            _client.UpdateReceived += async (_, update) => { await ProcessUpdates(update); };

            //await _client.
            // Waiting until we get enough events to be in 'authentication ready' state
            ReadyToAuthenticate.Wait();

            // We may not need to authenticate since TdLib persists session in 'td.binlog' file.
            // See 'TdlibParameters' class for more information, or:
            // https://core.telegram.org/tdlib/docs/classtd_1_1td__api_1_1tdlib_parameters.html
            if (_authNeeded)
            {
                // Interactively handling authentication
                await HandleAuthentication();
            }

            // Querying info about current user and some channels
            var currentUser = await GetCurrentUser();

            var fullUserName = $"{currentUser.FirstName} {currentUser.LastName}".Trim();
            Console.WriteLine($"Successfully logged in as [{currentUser.Id}] / [@{currentUser.Usernames}] / [{fullUserName}]");

            const int channelLimit = 15;
            var channels = GetChannels(channelLimit);
            //Console.WriteLine($"Chats:");

            await foreach (var channel in channels)
            {
                Console.WriteLine($"Id -> [{channel.Id}] -> [{channel.Title}] ({channel.UnreadCount} messages unread)");
            }

            Console.WriteLine("Press ENTER to exit from application");
            Console.ReadLine();
        }

        private static async Task HandleAuthentication()
        {
            // Setting phone number
            await _client.ExecuteAsync(new TdApi.SetAuthenticationPhoneNumber
            {
                PhoneNumber = PhoneNumber
            });

            // Telegram servers will send code to us
            Console.Write("Insert the login code: ");
            var code = Console.ReadLine();

            await _client.ExecuteAsync(new TdApi.CheckAuthenticationCode
            {
                Code = code
            });

            if (!_passwordNeeded) { return; }

            // 2FA may be enabled. Cloud password is required in that case.
            Console.Write("Insert the password: ");
            var password = Console.ReadLine();

            await _client.ExecuteAsync(new TdApi.CheckAuthenticationPassword
            {
                Password = password
            });
        }

        private static async Task ProcessUpdates(TdApi.Update update)
        {
            // Since Tdlib was made to be used in GUI application we need to struggle a bit and catch required events to determine our state.
            // Below you can find example of simple authentication handling.
            // Please note that AuthorizationStateWaitOtherDeviceConfirmation is not implemented.

            switch (update)
            {
                case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitTdlibParameters }:
                    // TdLib creates database in the current directory.
                    // so create separate directory and switch to that dir.
                    var filesLocation = Path.Combine(AppContext.BaseDirectory, "db");
                    var parameters = new TdApi.SetTdlibParameters
                    {
                        DatabaseDirectory = "tdlib_data",  // Папка для зберігання даних
                        FilesDirectory = "files",          // Папка для зберігання файлів
                        UseMessageDatabase = true,         // Використовувати базу даних для повідомлень
                        UseSecretChats = true,             // Використовувати секретні чати
                        ApiId = 19593366,                  // Твій api_id
                        ApiHash = "09c1c085b8ed844ece9de511362eee4a", // Твій api_hash
                        SystemLanguageCode = "en",         // Мова системи
                        DeviceModel = "Windows",           // Модель пристрою
                        SystemVersion = "10",              // Версія операційної системи
                        ApplicationVersion = "0.1",       // Версія твоєї програми
                    };
                    await _client.ExecuteAsync(new TdApi.SetTdlibParameters
                    {
                        ApiId = ApiId,
                        ApiHash = ApiHash,
                        DeviceModel = "Windows",
                        SystemVersion = "10",
                        SystemLanguageCode = "en",
                        UseSecretChats = true,
                        UseMessageDatabase = true,
                        ApplicationVersion = ApplicationVersion,
                        DatabaseDirectory = filesLocation,
                        FilesDirectory = filesLocation,
                        // More parameters available!
                    });
                    break;

                /*case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitEncryptionKey }:
                    await _client.ExecuteAsync(new TdApi.CheckDatabaseEncryptionKey());
                    break;*/

                case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPhoneNumber }:
                case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitCode }:
                    _authNeeded = true;
                    ReadyToAuthenticate.Set();
                    break;

                case TdApi.Update.UpdateAuthorizationState { AuthorizationState: TdApi.AuthorizationState.AuthorizationStateWaitPassword }:
                    _authNeeded = true;
                    _passwordNeeded = true;
                    ReadyToAuthenticate.Set();
                    break;

                case TdApi.Update.UpdateUser:
                    ReadyToAuthenticate.Set();
                    break;

                case TdApi.Update.UpdateConnectionState { State: TdApi.ConnectionState.ConnectionStateReady }:
                    // You may trigger additional event on connection state change
                    break;

                default:
                    // ReSharper disable once EmptyStatement
                    ;
                    // Add a breakpoint here to see other events
                    break;
            }
        }

        private static async Task<TdApi.User> GetCurrentUser()
        {
            return await _client.ExecuteAsync(new TdApi.GetMe());
        }

        private static async IAsyncEnumerable<TdApi.Chat> GetChannels(int limit)
        {
            var chats = await _client.ExecuteAsync(new TdApi.GetChats
            {
                Limit = limit
            });

            foreach (var chatId in chats.ChatIds)
            {
                var chat = await _client.ExecuteAsync(new TdApi.GetChat
                {
                    ChatId = chatId
                });

                if (chat.Type is TdApi.ChatType.ChatTypeSupergroup or TdApi.ChatType.ChatTypeBasicGroup or TdApi.ChatType.ChatTypePrivate)
                {
                    yield return chat;
                }
            }
        }
    }

}