using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

/*
 * TcpClientExクラスを使わずに、System.Net.Sockets.TcpClientクラスを直接使用
 * AcceptTcpClientAsyncメソッドを使って非同期でクライアント接続を受け付ける
 * ReceiveCallbackメソッドで受信データの処理を行う際に、StateObjectクラスを使ってデータを渡す
*/
namespace ConsoleChatServer {
    class Program {
        const int BUFFER_SIZE = 1024;

        /// <summary>
        /// TCPサーバー。
        /// </summary>
        private static TcpListener Server { get; set; }

        /// <summary>
        /// 接続中クライアントリスト。
        /// </summary>
        private static List<TcpClient> ClientList { get; set; }

        /// <summary>
        /// Main 非同期
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        static async Task Main(string[] args) {
            ClientList = new List<TcpClient>();

            // ローカルIPv4アドレスを取得する
            //string localIPv4 = GetLocalIPv4();
            string localIPv4 = GetLocalWiFiIPv4();

            if (!string.IsNullOrEmpty(localIPv4)) {
                Console.WriteLine($"ローカルIPv4アドレス: {localIPv4}");
            }
            else {
                Console.WriteLine("ローカルIPv4アドレスが見つかりませんでした。");
                localIPv4 = "127.0.0.1";
            }

            // 接続設定
            //Console.Write("サーバーアドレスを入力してください: ");
            //string localIPv4 = Console.ReadLine();

            Console.Write("待ち受けポート番号を入力してください: ");
            int port = int.Parse(Console.ReadLine());

            // サーバーを作成して監視開始
            var localEndPoint = new IPEndPoint(IPAddress.Parse(localIPv4), port);

            Server = new TcpListener(localEndPoint);
            Server.Start();

            Console.WriteLine($"サーバーを開始しました。ポート {port} で接続を待ちます...");

            // 接続受付ループ開始
            await AcceptWaitLoop();

            Console.ReadLine(); // サーバーを終了するために入力待ち
        }

        private static async Task AcceptWaitLoop() {
            Console.WriteLine("接続受け入れ開始。");

            // サーバーが監視中の間は接続を受け入れ続ける
            while (Server != null) {
                try {
                    // 非同期で接続を待ち受ける
                    var client = await Server.AcceptTcpClientAsync();

                    // 接続ログを出力
                    Console.WriteLine($"{client.Client.RemoteEndPoint}からの接続");

                    // 接続中クライアントを追加
                    ClientList.Add(client);

                    // クライアントからのデータ受信を待機
                    ReceiveData(client);

                    // 接続中クライアント(接続したクライアント以外)に対してクライアントが接続した情報を送信する
                    SendDataToAllClient(client, $"{client.Client.RemoteEndPoint}がサーバーに接続しました。");
                }
                catch (Exception ex) {
                    Console.WriteLine($"接続受け入れでエラーが発生しました。{ex.Message}");
                    break;
                }
            }

            Console.WriteLine("接続受け入れ終了。");
        }

        /// <summary>
        /// ブロードキャスト
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="text"></param>
        private static void SendDataToAllClient(TcpClient sender, string text) {
            byte[] buffer = Encoding.UTF8.GetBytes(text);

            foreach (var client in ClientList.Where(c => c != sender)) {
                // データ送信
                client.Client.Send(buffer);

                // 送信ログを出力
                Console.WriteLine($"{client.Client.RemoteEndPoint}にデータ送信>>{text}");
            }
        }

        /// <summary>
        /// データ受信
        /// </summary>
        /// <param name="client"></param>
        private static void ReceiveData(TcpClient client) {
            var state = new StateObject { Client = client };
            client.Client.BeginReceive(state.Buffer, 0, StateObject.BufferSize, SocketFlags.None, ReceiveCallback, state);
        }

        /// <summary>
        /// データ受信コールバック
        /// データ受信処理
        /// </summary>
        /// <param name="result"></param>
        private static void ReceiveCallback(IAsyncResult result) {
            StateObject state = result.AsyncState as StateObject;
            TcpClient client = state.Client;

            try {
                int bytesRead = client.Client.EndReceive(result);

                // 受信データが0byteの場合切断と判定
                if (bytesRead == 0) {
                    // 切断ログを出力
                    Console.WriteLine($"{client.Client.RemoteEndPoint}からの切断");

                    // 接続中クライアントを削除
                    ClientList.Remove(client);

                    // 接続中クライアント(切断したクライアント以外)に対してクライアントが切断した情報を送信する
                    SendDataToAllClient(client, $"{client.Client.RemoteEndPoint}がサーバーから切断しました。");

                    // データ受信を終了
                    return;
                }

                // 受信データを出力
                string receivedData = Encoding.UTF8.GetString(state.Buffer, 0, bytesRead);
                Console.WriteLine($"{client.Client.RemoteEndPoint}からデータ受信<<{receivedData}");

                // 接続中クライアント(送信したクライアント以外)に対して受信したデータを送信する
                SendDataToAllClient(client, receivedData);

                // サーバーが監視中の場合、再度クライアントからのデータ受信を待機
                ReceiveData(client);
            }
            catch (Exception ex) {
                Console.WriteLine($"エラーが発生しました。{ex.Message}");
            }
        }

        /// <summary>
        /// 内部クラス
        /// コールバックへの引き渡し情報
        /// IAsyncResult.AsyncStateプロパティからStateObjectインスタンスを取得できる
        /// </summary>
        private class StateObject {
            public const int BufferSize = BUFFER_SIZE;
            public byte[] Buffer = new byte[BufferSize];
            public TcpClient Client;
        }

        /// <summary>
        /// サーバのIPアドレス取得
        /// </summary>
        /// <returns></returns>
        private static string GetLocalIPv4() {
            // ホスト名からIPアドレスを取得する
            string hostName = Dns.GetHostName();
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);

            // IPv4アドレスを探す
            foreach (IPAddress ipAddress in hostEntry.AddressList) {
                // IPv4アドレスであり、ループバックアドレス(127.0.0.1)でない場合
                if (ipAddress.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ipAddress)) {
                    return ipAddress.ToString();
                }
            }

            // 見つからなかった場合は空文字列を返す
            return string.Empty;
        }

        /// <summary>
        ///サーバのIPアドレス取得(Wi-Fi)
        /// </summary>
        /// <returns></returns>
        private static string GetLocalWiFiIPv4() {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces()) {
                // Wi-Fiインターフェイスかどうかを判定
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 && ni.OperationalStatus == OperationalStatus.Up) {
                    Console.WriteLine($"Wi-Fiインターフェイス: {ni.Name}");

                    // Wi-Fiインターフェイスのプロパティを取得
                    IPInterfaceProperties props = ni.GetIPProperties();
                    foreach (UnicastIPAddressInformation ip in props.UnicastAddresses) {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork) {
                            Console.WriteLine($"IPアドレス: {ip.Address}");
                            return ip.Address.ToString();
                        }
                    }
                }
            }

            // 見つからなかった場合は空文字列を返す
            return string.Empty;
        }

    }
}