using UnityEngine.UI;
using System;
using System.Text;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using System.Drawing;
using System.Linq;
using UnityEngine;

namespace generator
{
    public class QrCodergen : MonoBehaviour
    {
        private string m_auth_key;
        private string m_auth_host = "wss://hive-auth.arcange.eu"; // HAS server we are connecting to
        public InputField txtUsername;
        public RawImage picQRCode;
        public Button connectButton;
        public Text errorMessage;
        public Text tokenText;

        private ClientWebSocket socket;

        void Start()
        {
            connectButton.onClick.AddListener(OnConnectButtonClick);
          
        }

        // ...
        void ProcessMessage(string msg)
        {
            Debug.Log(msg);
            JObject JMsg = JObject.Parse(msg);

            switch ((string)JMsg["cmd"])
            {
                case "auth_wait":
                    try
                    {
                        // Update QRCode
                        string json =
                            new JObject(
                                new JProperty("account", txtUsername.text),
                                new JProperty("uuid", JMsg["uuid"]),
                                new JProperty("key", m_auth_key),
                                new JProperty("host", m_auth_host)
                                ).ToString();
                        Debug.Log("JSON payload: " + json);

                        string URI = "has://auth_req/" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
                        Debug.Log("QR code URI: " + URI);

                        // Use ZXing QR code generator
                        var writer = new BarcodeWriter
                        {
                            Format = BarcodeFormat.QR_CODE,
                            Options = new EncodingOptions
                            {
                                Height = 256,
                                Width = 256,
                                Margin = 1
                            }
                        };

                        var qrCodeTexture = new Texture2D(256, 256);
                        var encoded = new Texture2D(256, 256, TextureFormat.RGB24, false, true);
                        var color32 = writer.Write(URI);
                        qrCodeTexture.SetPixels32(color32);

                        qrCodeTexture.Apply();

                        if (picQRCode != null)
                        {
                            picQRCode.texture = qrCodeTexture;
                        }

                    }
                    catch (Exception ex)
                    {
                        Debug.LogError("Failed to process auth_wait message: " + ex.ToString());
                        errorMessage.text = "Failed to process auth_wait message: " + ex.Message;
                    }
                    break;
                case "auth_ack":
                    try
                    {
                        picQRCode.gameObject.SetActive(false);
                        // Try to decrypt and parse payload data
                        string decrypted = CryptoJS.Decrypt((string)JMsg["data"], m_auth_key);
                        JObject JData = JObject.Parse(decrypted);
                        string token = (string)JData["token"];
                        ulong expire = (ulong)JData["expire"];
                        Debug.Log(string.Format("Authenticated with token: {0}", token));
                        tokenText.text = string.Format("Authenticated with token: {0}", token);
                    }
                    catch (Exception ex)
                    {
                        picQRCode.gameObject.SetActive(false);
                        // Decryption failed - ignore message
                        Debug.Log("Decryption failed: " + ex.Message);
                    }
                    break;
                case "auth_err":
                    picQRCode.gameObject.SetActive(false);
                    string error = (string)JMsg["error"];
                    Debug.Log("Authentication error: " + error);
                    errorMessage.text = error;
                    break;
                case "auth_status":
                    try
                    {
                        string status = (string)JMsg["status"];
                        Debug.Log("Authentication status: " + status);
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("Error processing authentication status message: " + ex.Message);
                    }
                    break;
                default:
                    Debug.Log("Unknown command: " + (string)JMsg["cmd"]);
                    break;
            }
        }
        static async Task Send(ClientWebSocket socket, string data) =>
                    await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(data)), WebSocketMessageType.Text, true, CancellationToken.None);

        async Task Receive(ClientWebSocket socket)
        {
            var buffer = new ArraySegment<byte>(new byte[2048]);
            do
            {
                WebSocketReceiveResult result;
                using (MemoryStream ms = new MemoryStream())
                {
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    ms.Seek(0, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(ms, Encoding.UTF8))
                    {
                        ProcessMessage(await reader.ReadToEndAsync());
                    }
                }
            } while (true);
        }
        


        async void OnConnectButtonClick()
        {
            // Hide error message
            errorMessage.text = "";

            // Check if the input field is not empty
            if (string.IsNullOrEmpty(txtUsername.text))
            {
                errorMessage.text = "Please enter a username.";
                return;
            }

            using (ClientWebSocket ws = new ClientWebSocket())
            {
                try
                {
                    // Create a new authentication key
                    m_auth_key = Guid.NewGuid().ToString();
                    await ws.ConnectAsync(new Uri(m_auth_host), CancellationToken.None);

                    // Create auth_req_data
                    string auth_req_data =
                        new JObject(
                            new JProperty("app",
                                new JObject(
                                    new JProperty("name", "has-demo-dotnet"),
                                    new JProperty("description", "Demo - HiveAuth with .NET")
                                )
                            )
                        //,
                        //new JProperty("token", null),		// Initialize this property if you already have an HiveAuth token
                        //new JProperty("challenge", null)	// Initialize this proporty if you have a challenge
                        ).ToString();

                    // Encrypt auth_req_data using our authentication key
                    auth_req_data = CryptoJS.Encrypt(auth_req_data, m_auth_key);

                    // Prepare HAS payload
                    string payload =
                        new JObject(
                            new JProperty("cmd", "auth_req"),
                            new JProperty("account", txtUsername.text),
                            new JProperty("data", auth_req_data)
                        ).ToString();
                    // Send the auth_req to the HAS server
                    await Send(ws, payload);
                    // Wait for request processing
                    await Receive(ws);
                    Debug.Log("receive completed");
                }
                catch (Exception ex)
                {

                    // Display error message
                    errorMessage.text = "Error: " + ex.Message;
                    Debug.LogException(ex);
                }
                
                
            }
        }
        
    }
}
