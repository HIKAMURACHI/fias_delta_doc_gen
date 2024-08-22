using Newtonsoft.Json.Linq;
using System;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.IO;
using System.Runtime.Remoting.Messaging;

namespace fias_delta_doc_gen
{
    internal class Program
    {
        const string unpackPath = @"./unpack";
        static void Main(string[] args)
        {
            string jsonData = GetLastDownloadFileInfo("https://fias.nalog.ru/WebServices/Public/GetLastDownloadFileInfo");
            JObject jsonObj = JObject.Parse(jsonData);
            string garXMLDeltaURL = jsonObj["GarXMLDeltaURL"].ToString();
            string fileDate = jsonObj["Date"].ToString();
            string archiveName = garXMLDeltaURL.Split('/').Last();

            //удалить если существуют предыдущие файлы
            if (File.Exists(archiveName))
            {
                File.Delete(archiveName);
            }
            if (Directory.Exists(unpackPath))
                Directory.Delete(unpackPath, true);

            //скачать и проверить наличие
            DownloadFileByUrl(garXMLDeltaURL);
            if (!File.Exists(archiveName)) {
                Console.WriteLine("File is not exists");
                return;
            }

            //распаковка и парсинг
            UnzipArchive(archiveName);
            List<ObjectLevel> levels = ParseObjectLevels(unpackPath);
            List<ObjectAddress> address = ParseActiveObjectAddress(GetObjectAddressFiles(unpackPath));

            //вывод данных            
            WriteToFile(@"./result.html", GenResultPage(levels, address, fileDate));
            OpenFile(Path.Combine(Directory.GetCurrentDirectory(), "result.html"));
        }

        private static string GetLastDownloadFileInfo(string url)
        {
            using (WebClient wc = new WebClient())
            {
                //получаем json
                return wc.DownloadString(url);
            }
        }
        private static void DownloadFileByUrl(string url)
        {
            Console.WriteLine("Downloading");
            using (var client = new WebClient())
            {
                //Загрузка файла
                client.DownloadFile(url, url.Split('/').Last());
            }
        }
        private static void UnzipArchive(string archivePath)
        {            
            Console.WriteLine($"WAIT, archive unpacked ...");
            //Распаковка
            ZipFile.ExtractToDirectory($"./{archivePath}", unpackPath);
        }
        private static List<ObjectLevel> ParseObjectLevels(string folderPath)
        {
            Console.WriteLine("Parse Object Levels");
            try
            {
                string objectLevelFileName = Directory.GetFiles(folderPath, "AS_OBJECT_LEVELS_*", SearchOption.AllDirectories).FirstOrDefault();
                // Загружаем XML-документ
                XDocument doc = XDocument.Load(objectLevelFileName);

                // Получаем список элементов "OBJECT_LEVEL"
                var objectLevels = doc.Descendants("OBJECTLEVEL");

                List<ObjectLevel> levels = new List<ObjectLevel>();
                // Получаем информацию о каждом элементе "OBJECT_LEVEL"
                foreach (var objectLevel in objectLevels)
                {                    
                    int level = int.Parse(objectLevel.Attribute("LEVEL").Value);
                    string name = objectLevel.Attribute("NAME").Value ?? string.Empty;                    
                    bool isActive = bool.Parse(objectLevel.Attribute("ISACTIVE").Value);
                    levels.Add(new ObjectLevel(level, name, isActive));
                }
                return levels;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Read file error: {ex.Message}");
            }
            return null;
        }        
        private static List<string> GetObjectAddressFiles(string folderPath)
        {
            Console.WriteLine("Get object files");
            string[] folders = Directory.GetDirectories(folderPath);
            List<string> filesPaths = new List<string>();
            foreach (string folder in folders)
            {
                string[] files = Directory.GetFiles(folder);

                // Фильтруем файлы по шаблону AS_ADDR_OBJ_ 
                IEnumerable<string> filteredFiles = files
                    .Where(f => Path.GetFileNameWithoutExtension(f).StartsWith("AS_ADDR_OBJ_") &&
                                !Path.GetFileNameWithoutExtension(f).StartsWith("AS_ADDR_OBJ_DIVISION_") &&
                                !Path.GetFileNameWithoutExtension(f).StartsWith("AS_ADDR_OBJ_PARAMS_"));                
                if(filteredFiles.Count() > 0)
                    filesPaths.Add(filteredFiles.First());
            }
            return filesPaths;
        }
        private static List<ObjectAddress> ParseActiveObjectAddress(IEnumerable<string> files)
        {
            Console.WriteLine("Parse Object Address");
            List<ObjectAddress> objects = new List<ObjectAddress>();
            foreach (string file in files)
            {
                // Загружаем XML-документ
                XDocument doc = XDocument.Load(file);

                // Получаем список элементов "OBJECT"
                var objectAddresses = doc.Descendants("OBJECT");
                
                // Получаем информацию о каждом элементе "OBJECT"
                foreach (var address in objectAddresses)
                {
                    int level = int.Parse(address.Attribute("LEVEL").Value);
                    string name = address.Attribute("NAME").Value ?? string.Empty;
                    string typeName = address.Attribute("TYPENAME").Value ?? string.Empty;
                    int isActive = int.Parse(address.Attribute("ISACTIVE").Value);

                    if (isActive == 1)
                        objects.Add(new ObjectAddress(level, name, typeName, isActive));
                }                
            }
            //нужно удалить дубликаты, но в тз этого нет, слово клиента закон
            //Сортировка по имени
            objects.Sort((x,y) => string.Compare(x.name, y.name));
            return objects;
        }
        private static string GenResultPage(List<ObjectLevel> levels, List<ObjectAddress> address, string fileDate)
        {
            StringBuilder html = new StringBuilder();

            // Заголовок документа
            html.Append("<!DOCTYPE html>\n");
            html.Append("<html>\n");
            html.Append("<head>\n");
            html.Append("<title>Отчет</title>\n");
            html.Append("</head>\n");
            html.Append("<body>\n");
            html.Append($"<h1>Отчет по добавленным адресным объектам {fileDate}</h1>\n");

            foreach (ObjectLevel level in levels)
            {                
                html.Append($"<h2>{level.name}</h2>\n");
                // Таблица
                html.Append("<table border=\"1\">\n");
                html.Append("<thead>\n");
                html.Append("<tr>\n");
                html.Append($"<th>Тип объекта</th>\n");
                html.Append($"<th>Наименование</th>\n");
                html.Append("</tr>\n");
                html.Append("</thead>\n");
                html.Append("<tbody>\n");
                foreach (ObjectAddress addr in address)
                {

                    if (level.level == addr.level)
                    {
                        html.Append("<tr>\n");
                        html.Append($"<td>{addr.typeName}</td>\n");
                        html.Append($"<td>{addr.name}</td>\n");
                        html.Append("</tr>\n");
                    }                    
                }                
                html.Append("</tbody>\n");
                html.Append("</table>\n");
            }

            // Конец документа
            html.Append("</body>\n");
            html.Append("</html>\n");

            return html.ToString();
        }
        private static void WriteToFile(string filePath, string content)
        {
            Console.WriteLine("Create html file");
            try
            {
                // Создание файла с помощью File.WriteAllText
                File.WriteAllText(filePath, content);                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error create result file: {ex.Message}");
            }
        }
        public static void OpenFile(string filePath)
        {
            try
            {
                // Открытие файла с помощью System.Diagnostics.Process
                System.Diagnostics.Process.Start(filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error open result file: {ex.Message}");
                Console.ReadKey();
            }
        }

        class ObjectLevel
        {
            public int level { get; set; }
            public string name { get; set; }
            public bool isActive { get; set; }
            
            public ObjectLevel(int level, string name, bool isActive)
            {
                this.level = level;
                this.name = name;
                this.isActive = isActive;
            }
        }
        class ObjectAddress
        {
            public int level { get; set ; }
            public string name { get; set; }
            public string typeName { get; set; }
            public int isActive { get; set; }

            public ObjectAddress(int level, string name, string typeName, int isActive)
            {
                this.level = level;
                this.name = name;
                this.typeName = typeName;
                this.isActive = isActive;
            }
        }
    }
}
