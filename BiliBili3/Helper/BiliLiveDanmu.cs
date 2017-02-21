﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Windows.UI.Popups;
using System.Net;
using System.Diagnostics;
using System.Xml;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Windows.UI;

namespace BiliBili3.Helper
{
    /*
     使用流程
     1、初始化此类
     2、订阅HasDanmu事件
     3、调用Start方法开始
     4、
         */
   public  class BiliLiveDanmu : IDisposable
    {

        public enum LiveDanmuTypes
        {
            //观众
            Viewer,
            //弹幕
            Danmu,
            //礼物
            Gift,
            //欢迎
            Welcome,
            //系统信息
            SystemMsg
        }
        public delegate void HasDanmuHandel(LiveDanmuModel value);
        public event HasDanmuHandel HasDanmu;

        private StreamSocket _clientSocket;
        private DispatcherTimer _timer;

        private int _roomId;
        public BiliLiveDanmu()
        {

        }

        //开始
        public async void Start(int roomid,long userId)
        {
            try
            {
                _roomId = roomid;

                HostName serverHost = new HostName(await GetDanmuServer());  //设置服务器IP  

                _clientSocket = new StreamSocket();
                await _clientSocket.ConnectAsync(serverHost, "788");  //设置服务器端口号  
                _StartState = true;

            }
            catch (Exception)
            {
                _StartState = false;
            }
            
            if (_StartState)
            {
                if (SendJoinChannel(roomid, userId))
                {
                    SendHeartbeatAsync();
                    _timer = new DispatcherTimer();
                    _timer.Interval = new TimeSpan(0, 0, 30);
                    _timer.Tick += Timer_Tick;
                    _timer.Start();
                    await Task.Run(()=> { Listen(); });
                }
                else
                {
                    throw new NotSupportedException("无法加入频道");
                }

            }
            else
            {
                throw new NotSupportedException("无法连接服务器");
            }


        }

        private async void Listen()
        {
            Stream _netStream = _clientSocket.InputStream.AsStreamForRead(1024);
            byte[] stableBuffer = new byte[1024];
            while (true)
            {

                if (!_StartState)
                {
                    break;
                }
                try
                {
                    _netStream.ReadB(stableBuffer, 0, 4);
                    var packetlength = BitConverter.ToInt32(stableBuffer, 0);
                    packetlength = IPAddress.NetworkToHostOrder(packetlength);

                    if (packetlength < 16)
                    {
                        throw new NotSupportedException("协议失败: (L:" + packetlength + ")");
                    }

                    _netStream.ReadB(stableBuffer, 0, 2);//magic
                    _netStream.ReadB(stableBuffer, 0, 2);//protocol_version 

                    _netStream.ReadB(stableBuffer, 0, 4);
                    var typeId = BitConverter.ToInt32(stableBuffer, 0);
                    typeId = IPAddress.NetworkToHostOrder(typeId);

                    _netStream.ReadB(stableBuffer, 0, 4);//magic, params?

                    var playloadlength = packetlength - 16;
                    if (playloadlength == 0)
                    {

                        //continue;//没有内容了
                    }

                    typeId = typeId - 1;//magic, again (为啥要减一啊) 
                    var buffer = new byte[playloadlength];
                    _netStream.ReadB(buffer, 0, playloadlength);
                    var json = Encoding.UTF8.GetString(buffer, 0, playloadlength);
                    if (typeId == 2)
                    {
                        var viewer = BitConverter.ToUInt32(buffer.Take(4).Reverse().ToArray(), 0); //观众人数
                        if (HasDanmu != null)
                        {
                            HasDanmu(new LiveDanmuModel() { type = LiveDanmuTypes.Viewer, viewer = Convert.ToInt32(viewer) });
                        }
                        Debug.WriteLine(viewer);
                    }
                    else
                    {
                        if (json.Trim().Length != 0)
                        {
                            Debug.WriteLine(json);
                            JObject obj = JObject.Parse(json);
                            switch (obj["cmd"].ToString())
                            {
                                case "DANMU_MSG":
                                    var v = new DanmuMsgModel();
                                    if (obj["info"] != null && obj["info"].ToArray().Length != 0)
                                    {
                                        v.text = obj["info"][1].ToString();
                                        if (obj["info"][2] != null && obj["info"][2].ToArray().Length != 0)
                                        {
                                            v.username = obj["info"][2][1].ToString();
                                            //v.usernameColor = GetColor(obj["info"][2][0].ToString());
                                            if (obj["info"][2][3] != null && Convert.ToInt32(obj["info"][2][3].ToString()) == 1)
                                            {
                                                v.vip = "老爷";
                                            }
                                            if (obj["info"][2][4] != null && Convert.ToInt32(obj["info"][2][4].ToString()) == 1)
                                            {
                                                v.vip = "年费老爷";
                                            }
                                            if (obj["info"][2][2] != null && Convert.ToInt32(obj["info"][2][2].ToString()) == 1)
                                            {
                                                v.vip = "房管";
                                            }
                                        }
                                        if (obj["info"][3] != null && obj["info"][3].ToArray().Length != 0)
                                        {
                                            v.medal = obj["info"][3][1].ToString() + obj["info"][3][0].ToString();
                                            v.medalColor = obj["info"][3][4].ToString();
                                        }
                                        if (obj["info"][4] != null && obj["info"][4].ToArray().Length != 0)
                                        {
                                            v.ul = "UL" + obj["info"][4][0].ToString();
                                            v.ulColor = obj["info"][4][2].ToString();
                                        }
                                        if (obj["info"][5] != null && obj["info"][5].ToArray().Length != 0)
                                        {
                                            v.title = obj["info"][5][0].ToString();
                                        }
                                        if (HasDanmu != null)
                                        {
                                            HasDanmu(new LiveDanmuModel() { type = LiveDanmuTypes.Danmu, value = v });
                                        }
                                    }

                                    break;
                                case "SEND_GIFT":
                                    var g = new GiftMsgModel();
                                    if (obj["data"] != null)
                                    {
                                        g.uname = obj["data"]["uname"].ToString();
                                        g.action = obj["data"]["action"].ToString();
                                        g.giftId = Convert.ToInt32(obj["data"]["giftId"].ToString());
                                        g.giftName = obj["data"]["giftName"].ToString();
                                        g.num = obj["data"]["num"].ToString();
                                        g.uid = obj["data"]["uid"].ToString();
                                        if (HasDanmu != null)
                                        {
                                            HasDanmu(new LiveDanmuModel() { type = LiveDanmuTypes.Gift, value = g });
                                        }
                                    }

                                    break;
                                case "WELCOME":
                                    var w = new WelcomeMsgModel();
                                    if (obj["data"] != null)
                                    {
                                        w.uname = obj["data"]["uname"].ToString();
                                        w.uid = obj["data"]["uid"].ToString();
                                        if (HasDanmu != null)
                                        {
                                            HasDanmu(new LiveDanmuModel() { type = LiveDanmuTypes.Welcome, value = w });
                                        }
                                    }
                                    break;
                                case "SYS_MSG":
                                    if (obj["msg"] != null)
                                    {
                                        if (HasDanmu != null)
                                        {
                                            HasDanmu(new LiveDanmuModel() { type = LiveDanmuTypes.SystemMsg, value = obj["msg"].ToString() });
                                        }
                                    }

                                    break;
                                default:
                                    break;
                            }
                        }

                    }




                }
                catch (Exception)
                {

                }


                await Task.Delay(50);
            }



        }
        ///// <summary>
        /////十进制转SolidColorBrush
        ///// </summary>
        ///// <param name="_color">输入10进制颜色</param>
        ///// <returns></returns>
        //public async Task<SolidColorBrush> GetColor(string _color)
        //{
        //    SolidColorBrush solid = new SolidColorBrush(new Color()
        //    {
        //        A = 255,
        //        R = 255,
        //        G = 255,
        //        B = 255
        //    });
        //    await Task.Run(() => {
        //        try
        //        {
        //            _color = Convert.ToInt32(_color).ToString("X2");
        //            if (_color.StartsWith("#"))
        //                _color = _color.Replace("#", string.Empty);
        //            int v = int.Parse(_color, System.Globalization.NumberStyles.HexNumber);
        //             solid = new SolidColorBrush(new Color()
        //            {
        //                A = Convert.ToByte(255),
        //                R = Convert.ToByte((v >> 16) & 255),
        //                G = Convert.ToByte((v >> 8) & 255),
        //                B = Convert.ToByte((v >> 0) & 255)
        //            });
        //            // color = solid;
        //            return solid;
        //        }
        //        catch (Exception)
        //        {
        //            return solid;
        //            // color = solid;

        //        }
        //    });


        //    return solid;

        //}


        private async Task<string> GetDanmuServer()
        {
            try
            {
                var chat = "http://live.bilibili.com/api/player?id=cid:" + _roomId;
                string results  = await WebClientClass.GetResults(new Uri(chat));
                var url = Regex.Match(results, "<server>(.*?)</server>", RegexOptions.Singleline).Groups[1].Value;
                if (url.Length != 0)
                {
                    return url;
                }
                else
                {
                    return "livecmt-2.bilibili.com";
                }
            }
            catch (Exception)
            {
                return "livecmt-2.bilibili.com";
            }
               
                

        }



        private void Timer_Tick(object sender, object e)
        {
            SendHeartbeatAsync();
        }

        private void SendHeartbeatAsync()
        {
            SendSocketData(2);
        }

        private bool SendJoinChannel(int channelId,long userId)
        {
            var r = new Random();

            long tmpuid =0;
            if (userId==0)
            {
                tmpuid = (long)(1e14 + 2e14 * r.NextDouble());
            }
            else
            {
                tmpuid = userId;
            }
            var packetModel = new { roomid = channelId, uid = tmpuid };
            var playload = JsonConvert.SerializeObject(packetModel);
            SendSocketData(7, playload);
            return true;
        }

        private void SendSocketData(int action, string body = "")
        {
            SendSocketData(0, 16, 1, action, 1, body);
        }
        private async void SendSocketData(int packetlength, short magic, short ver, int action, int param = 1, string body = "")
        {
            var playload = Encoding.UTF8.GetBytes(body);
            if (packetlength == 0)
            {
                packetlength = playload.Length + 16;
            }
            var buffer = new byte[packetlength];
            using (var ms = new MemoryStream(buffer))
            {
                //Array.Reverse(a)
                var b = BitConverter.GetBytes(buffer.Length).ToArray().Reverse().ToArray();

                ms.Write(b, 0, 4);
                b = BitConverter.GetBytes(magic).ToArray().Reverse().ToArray();
                ms.Write(b, 0, 2);
                b = BitConverter.GetBytes(ver).ToArray().Reverse().ToArray();
                ms.Write(b, 0, 2);
                b = BitConverter.GetBytes(action).ToArray().Reverse().ToArray();
                ms.Write(b, 0, 4);
                b = BitConverter.GetBytes(param).ToArray().Reverse().ToArray();
                ms.Write(b, 0, 4);
                if (playload.Length > 0)
                {
                    ms.Write(playload, 0, playload.Length);
                }
                DataWriter writer = new DataWriter(_clientSocket.OutputStream);  //实例化writer对象，以StreamSocket的输出流作为writer的方向  
                                                                                 // string content = "ABCDEFGH";  //发送一字符串  
                                                                                 //byte[] data = Encoding.UTF8.GetBytes(content);  //将字符串转换为字节类型，完全可以不用转换  
                writer.WriteBytes(buffer);  //写入字节流，当然可以使用WriteString直接写入字符串  
                await writer.StoreAsync();  //异步发送数据  
                writer.DetachStream();  //分离  
                writer.Dispose();  //结束writer  



                // _netStream.WriteAsync(buffer, 0, buffer.Length);
                //  _netStream.FlushAsync();
            }
        }





        bool _StartState = false;
        public void Dispose()
        {
            _StartState = false;


        }

        public class LiveDanmuModel
        {
            
            public LiveDanmuTypes type { get; set; }
            public int viewer { get; set; }
            public object value { get; set; }

        }
        public class DanmuMsgModel
        {
            public string text { get; set; }
            public string username { get; set; }//昵称
           // public SolidColorBrush usernameColor { get; set; }//昵称颜色

            public string ul { get; set;}//等级
            public string ulColor { get; set; }//等级颜色

            public string title { get; set; }//头衔id（对应的是CSS名）

            public string vip { get; set; }
            public string medal { get; set; }//勋章
            public string medalColor { get; set; }//勋章颜色
        }
        public class GiftMsgModel
        {
            public string uname { get; set; }
            public string giftName { get; set; }
            public string action { get; set; }
            public string num { get; set; }
            public int giftId { get; set; }
            public string uid { get; set; }
        }
        public class WelcomeMsgModel
        {
            public string uname { get; set; }
            public string isadmin { get; set; }
            public string uid { get; set; }
            public string svip { get; set; }
        }


    }

    public static class Utils
    {


        public static void ReadB(this Stream stream, byte[] buffer, int offset, int count)
        {
            if (offset + count > buffer.Length)
                throw new ArgumentException();
            var read = 0;
            while (read < count)
            {
                var available = stream.Read(buffer, offset, count - read);
                if (available == 0)
                {
                    throw new ObjectDisposedException(null);
                }
                read += available;
                offset += available;
            }
        }
    }

}
