using eDIAN.Core;
using eDIAN.Main;
using log4net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Xml.Linq;

namespace eDIAN.Data
{
    public class Win32Window : IWin32Window
    {
        public IntPtr Handle { get; }

        public Win32Window(IntPtr handle)
        {
            Handle = handle;
        }

        public Win32Window() 
        {
            Handle = CommonConstants.CAD_MAIN_WINDOW_HANDLE;
        }
    }

    public class Global
    {
        public static readonly Global instance;

        private static readonly ILog logger = PluginLogger.getLogger("Global", "application.log");

        // 다국적 제목을 저장하는 딕셔너리 
        private Dictionary<String, MessageData> titleDictionary;

        // 다국적 메시지를 저장하는 딕셔너리
        private Dictionary<String, MessageData> messageDictionary;

        public Win32Window mainWindow { get; }

        static Global()
        {
            instance = new Global();
        }

        public Global() 
        {
            Win32Window mainWindow = new Win32Window();

            this.titleDictionary = new Dictionary<string, MessageData>();

            this.messageDictionary = new Dictionary<string, MessageData>();

            String filePath = Path.Combine(CommonConstants.PLUGIN_PATH, "Resources", "global", "messages.xml");

            this.loadMessage(filePath);
        }

        private void loadMessage(String filePath) 
        {
            // 실행 파일 기준 경로 조합

            if (!File.Exists(filePath)) 
            {
                return;
            }

            XDocument xDocument = XDocument.Load(filePath);

            XElement rootTag = xDocument.Root;

            if (rootTag == null || rootTag.Name != "global")
            {
                return;
            }

            foreach (XElement groupTag in rootTag.Elements("group"))
            {
                // <messages type="message"> 의 type 속성 (필요시 사용)

                String groupType = (String)groupTag.Attribute("type") ?? "title";

                foreach (XElement nodeTag in groupTag.Elements("node"))
                {
                    String id = (String)nodeTag.Attribute("id");
                    String kind = (String)nodeTag.Attribute("kind") ?? "";
                    String titleId = (String)nodeTag.Attribute("title") ?? "";

                    if (String.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    String korMesg = "";
                    String engMesg = "";

                    foreach (XElement itemTag in nodeTag.Elements("item")) 
                    {
                        String lang = (String)itemTag.Attribute("lang");

                        if (String.IsNullOrEmpty(lang))
                        {
                            continue;
                        }

                        if (lang.Equals("kor")) 
                        {
                            korMesg = itemTag.Value;
                        }
                        else if (lang.Equals("eng"))
                        {
                            engMesg = itemTag.Value;
                        }
                    }

                    MessageData data = new MessageData(groupType, id, kind, titleId, korMesg, engMesg);

                    // titleDictionary 에 저장 (기존 키가 있으면 덮어씀)

                    if (groupType.Equals("title"))
                    {
                        if (titleDictionary.ContainsKey(id))
                        {
                            titleDictionary[id] = data;
                        }
                        else
                        {
                            titleDictionary.Add(id, data);
                        }
                    }
                    else if (groupType.Equals("message"))
                    {
                        if (messageDictionary.ContainsKey(id))
                        {
                            messageDictionary[id] = data;
                        }
                        else
                        {
                            messageDictionary.Add(id, data);
                        }
                    }
                }
            }
        }

        public MessageData getMessageData(String key) 
        {
            MessageData result = null;

            if (String.IsNullOrEmpty(key))
            {
                return result;
            }

            if (messageDictionary.TryGetValue(key, out MessageData messageData)) 
            {
                result = messageData;
            }

            return result;
        }

        public String getTitle(String key)
        {
            String result = "";

            if (String.IsNullOrEmpty(key))
            {
                return result;
            }

            if (titleDictionary.TryGetValue(key, out MessageData titleData))
            {
                if (CommonConstants.GLOBAL_KEY.Equals("KOR"))
                {
                    result = titleData.korMesg;
                }
                else if (CommonConstants.GLOBAL_KEY.Equals("ENG"))
                {
                    result = titleData.engMesg;
                }
            }

            return result;
        }

        public String getMessage(String key)
        {
            String result = "";

            if (String.IsNullOrEmpty(key))
            {
                return result;
            }

            if (messageDictionary.TryGetValue(key, out MessageData messageData))
            {
                if (CommonConstants.GLOBAL_KEY.Equals("KOR"))
                {
                    result = messageData.korMesg;
                }
                else if (CommonConstants.GLOBAL_KEY.Equals("ENG"))
                {
                    result = messageData.engMesg;
                }
            }

            return result;
        }

        public Dictionary<String, MessageData> getTitleDictionary()
        {
            return this.titleDictionary;
        }

        public Dictionary<String, MessageData> getMessgaeDictionary()
        {
            return this.messageDictionary;
        }
    }

    public class MessageHandler
    {
        public static DialogResult Show(String messageKey)
        {
            MessageData messageData = PluginApplication.global.getMessageData(messageKey);

            if (messageData == null)
            {
                return DialogResult.None;
            }

            String message = String.Empty;

            if (CommonConstants.GLOBAL_KEY.Equals("KOR"))
            {
                message = messageData.korMesg;
            }
            else if (CommonConstants.GLOBAL_KEY.Equals("ENG"))
            {
                message = messageData.engMesg;
            }

            return Show(message, messageData.titleId, messageData.kind);
        }

        public static DialogResult Show(String message, String titleId = null, String messageKind = null)
        {
            MessageBoxIcon icon = MessageBoxIcon.None;

            MessageBoxButtons button = MessageBoxButtons.OK;

            if (message == null)
            {
                return DialogResult.None;
            }

            String title = "edian++";

            if (String.IsNullOrEmpty(titleId) == false)
            {
                title = PluginApplication.global.getTitle(titleId) ?? title;
            }

            if (messageKind == null)
            {
                messageKind = "Info";
            }

            switch (messageKind)
            {
                case "Info":

                    icon = MessageBoxIcon.Information;
                    button = MessageBoxButtons.OK;

                    break;

                case "Question":

                    icon = MessageBoxIcon.Question;
                    button = MessageBoxButtons.YesNo;

                    break;

                case "Warning":

                    icon = MessageBoxIcon.Warning;
                    button = MessageBoxButtons.OK;

                    break;

                case "Error":

                    icon = MessageBoxIcon.Error;
                    button = MessageBoxButtons.OK;

                    break;

                default:

                    break;
            }

            return System.Windows.Forms.MessageBox.Show(PluginApplication.global.mainWindow, message, title, button, icon);
        }
    }


    public class MessageData 
    {
        public String type { get; set; } = String.Empty;

        public String id { get; set; } = String.Empty;

        public String kind { get; set; } = String.Empty;

        public String titleId { get; set; } = String.Empty;

        public String korMesg { get; set; } = String.Empty;

        public String engMesg { get; set; } = String.Empty;

        public MessageData(String type, String id, String kind, String titleId, String korMesg, String engMesg) 
        {
            this.type = type;
            this.id = id;
            this.kind = kind;
            this.titleId = titleId;
            this.korMesg = korMesg;
            this.engMesg = engMesg;
        }
        public MessageData(String korMesg, String engMesg)
        {
            this.korMesg = korMesg;
            this.engMesg = engMesg;
        }
    }
}
