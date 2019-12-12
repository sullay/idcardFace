using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Web;
using System.Net;
using System.Net.Sockets;
using AForge.Video.DirectShow;
using ArcsoftIDCardFace.SDKModels;
using ArcsoftIDCardFace.SDKUtil;
using ArcsoftIDCardFace.Utils;
using System.Configuration;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Fleck;
using Newtonsoft.Json;
namespace sinosecu
{
    class Program
    {
        //引擎Handle
        static IntPtr pEngine = IntPtr.Zero;
        //身份证图片byte
        static byte[] byteImage = new byte[40000];
        public static List<IWebSocketConnection> allSockets;
        public static Bitmap GetImageFromBase64(string base64string)
        {
            byte[] b = Convert.FromBase64String(base64string);
            MemoryStream ms = new MemoryStream(b);
            Bitmap bitmap = new Bitmap(ms);
            return bitmap;
        }
        static void InitEngines()
        {
            //读取配置文件
            AppSettingsReader reader = new AppSettingsReader();
            string appId = (string)reader.GetValue("APP_ID", typeof(string));
            string sdkKey64 = (string)reader.GetValue("SDKKEY64", typeof(string));
            string sdkKey32 = (string)reader.GetValue("SDKKEY32", typeof(string));
            var is64CPU = Environment.Is64BitProcess;
            if (is64CPU)
            {
                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(sdkKey64))
                {

                    MessageBox.Show("请在App.config配置文件中先配置APP_ID和SDKKEY64!");
                    return;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(sdkKey32))
                {

                    MessageBox.Show("请在App.config配置文件中先配置APP_ID和SDKKEY32!");
                    return;
                }
            }

            //激活引擎    如出现错误，1.请先确认从官网下载的sdk库已放到对应的bin中，2.当前选择的CPU为x86或者x64
            int retCode = 0;

            try
            {
                retCode = ASIDCardFunctions.ArcSoft_FIC_Activate(appId, is64CPU ? sdkKey64 : sdkKey32);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                if (ex.Message.IndexOf("无法加载 DLL") > -1)
                {
                    MessageBox.Show("请将sdk相关DLL放入bin对应的x86或x64下的文件夹中!");
                }
                else
                {
                    MessageBox.Show("激活引擎失败!");
                }
                return;
            }
            Console.WriteLine("Activate Result:" + retCode);

            //初始化引擎
            retCode = ASIDCardFunctions.ArcSoft_FIC_InitialEngine(ref pEngine);
            Console.WriteLine("InitEngine Result:" + retCode);
            if (retCode != 0)
            {
                MessageBox.Show(string.Format("引擎初始化失败!错误码为:{0}\n", retCode));
                return;
            }

        }
        static void start(Img imgInfo)
        {
            var image = GetImageFromBase64(imgInfo.srcImg.Replace("data:image/png;base64,", "").Replace("data:image/jgp;base64,", "")
                .Replace("data:image/jpg;base64,", "").Replace("data:image/jpeg;base64,", ""));
            // Image image = Image.FromFile("C:/xxx/1.jpg");
            int result2 = IDCardUtil.IdCardDataFeatureExtraction(pEngine, image);
            if (result2 != 0)
            {
                Console.WriteLine("idCard失败");
                allSockets.ToList().ForEach(s => s.Send("idCard失败"));
            }
            var bitmap = GetImageFromBase64(imgInfo.faceImg.Replace("data:image/png;base64,", "").Replace("data:image/jgp;base64,", "")
                .Replace("data:image/jpg;base64,", "").Replace("data:image/jpeg;base64,", ""));

            // Bitmap bitmap = new Bitmap(@"C:\xxx\xxx.bmp");
            if (bitmap == null)
            {
                return;
            }
            float offsetX = image.Width * 1f / bitmap.Width;
            float offsetY = image.Height * 1f / bitmap.Height;
            AFIC_FSDK_FACERES faceInfo = new AFIC_FSDK_FACERES();
            int result = IDCardUtil.FaceDataFeatureExtraction(pEngine, false, bitmap, ref faceInfo);
            if (result == 0 && faceInfo.nFace > 0)
            {
                float pSimilarScore = 0;
                int pResult = 0;
                float threshold = 0.82f;
                result = IDCardUtil.FaceIdCardCompare(ref pSimilarScore, ref pResult, pEngine, threshold);
                if (result == 0)
                {
                    Console.WriteLine(pSimilarScore);
                    allSockets.ToList().ForEach(s => s.Send("相似度:" + pSimilarScore.ToString()));
                    if (threshold > pSimilarScore)
                    {
                        Console.WriteLine("失败");
                    }
                    else
                    {
                        Console.WriteLine("成功");
                    }
                }
            }
            else
            {
                allSockets.ToList().ForEach(s => s.Send("不存在人脸"));
                Console.WriteLine("不存在人脸");
            }
        }
        class Img
        {
            public string srcImg { get; set; }
            public string faceImg { get; set; }
        }
        static void Main(string[] args)
        {
            InitEngines();
            //start();
            FleckLog.Level = LogLevel.Error;
            allSockets = new List<IWebSocketConnection>();
            var server = new WebSocketServer("ws://192.168.0.149:9000");
            server.Start(socket =>
            {
                socket.OnOpen = () =>
                {
                    Console.WriteLine("Open!");
                    allSockets.Add(socket);
                };
                socket.OnClose = () =>
                {
                    Console.WriteLine("Close!");
                    allSockets.Remove(socket);
                };
                socket.OnMessage = message =>
                {
                    // allSockets.ToList().ForEach(s => s.Send("Echo: " + message));
                    Img imgInfo = JsonConvert.DeserializeObject<Img>(message);
                    if (imgInfo.faceImg.Length > 0 && imgInfo.srcImg.Length > 0)
                    {
                        start(imgInfo);
                    }
                    else {
                        allSockets.ToList().ForEach(s => s.Send("參數有誤"));
                    }
                };
            });
            while (true) {
            }
        }
    }
}
