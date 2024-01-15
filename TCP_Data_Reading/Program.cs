using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TcpDataFromMultipleSensors
{
    static class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine($"Application started with code 3012....");
            AppConfig appConfig = LoadConfiguration();
            List<SensorInfo> sensors = appConfig.Sensors;

            List<Task> sensorTasks = new List<Task>();

            foreach (var sensor in sensors)
            {
                Task task = ConnectAndReadDataAsync(sensor);
                sensorTasks.Add(task);
            }

            await Task.WhenAll(sensorTasks);
        }

        static AppConfig LoadConfiguration()
        {
            string Directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string configFile = Path.Combine(Directory, "appconfig.json");

            using (StreamReader reader = new StreamReader(configFile))
            {
                string json = reader.ReadToEnd();
                return JsonSerializer.Deserialize<AppConfig>(json);
            }
        }
        static async Task ConnectAndReadDataAsync(SensorInfo sensor)
        {
            TimeSpan retryInterval = TimeSpan.FromSeconds(20); // 20 sec retry interval

            while (true) // Infinite loop for retrying
            {
                try
                {
                    using (TcpClient tcpClient = new TcpClient())
                    {
                        var connectTask = tcpClient.ConnectAsync(sensor.IPAddress, sensor.Port);
                        await Task.WhenAny(connectTask, Task.Delay(retryInterval));

                        if (tcpClient.Connected)
                        {
                            // Successfully connected
                            Console.WriteLine($"{sensor.IPAddress} connected");

                            await ReadDataAsync(tcpClient, sensor);
                            return;
                        }
                        Console.WriteLine($"Error connecting/reading data from {sensor.Name} = ip : {sensor.IPAddress} at {DateTime.Now}");
                        Console.WriteLine($"Retrying to connect {sensor.Name} = ip : {sensor.IPAddress}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error connecting/reading data from {sensor.Name} = ip : {sensor.IPAddress} : {ex.Message}");
                }

                // Wait for the specified retry interval
                await Task.Delay(retryInterval);
            }
        }


        static async Task ReadDataAsync(TcpClient tcpClient, SensorInfo sensor)
        {
            using (NetworkStream stream = tcpClient.GetStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                while (tcpClient.Connected)
                {
                    string data = await reader.ReadLineAsync();
                    Console.WriteLine("data :" + data);
                    string pattern = @"\b(\d{13})\b";
                    Match match = Regex.Match(data, pattern);
                    string extractedPart = match.Value.Trim();
                    if (extractedPart.Length == 13)
                    {
                        SendDataToWebApi(sensor.IPAddress, extractedPart);
                        await Task.Delay(TimeSpan.FromMilliseconds(50));
                    }
                }
            }
        }

        static async Task SendDataToWebApi(string IPAddress, string data)
        {
            using (HttpClient client = new HttpClient())
            {
                AppConfig appConfig = LoadConfiguration();
                client.BaseAddress = new Uri(appConfig.BaseUrl);
                try
                {
                    SensorData sensorData = new SensorData { IPAddress = IPAddress, data = data };
                    string jsonData = JsonSerializer.Serialize(sensorData);
                    StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");

                    var res = await client.PostAsync(appConfig.ApiUrl, content);
                    Console.WriteLine($"Barcode send to {appConfig.BaseUrl}{appConfig.ApiUrl} and getting response code {res.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while sending data to {appConfig.BaseUrl}{appConfig.ApiUrl} : {ex.Message}");
                }
            }
        }
    }
    class SensorInfo
    {
        public string Name { get; }
        public string IPAddress { get; }
        public int Port { get; }

        public SensorInfo(string name, string ipAddress, int port)
        {
            Name = name;
            IPAddress = ipAddress;
            Port = port;
        }
    }
    class AppConfig
    {
        public string ApiUrl { get; set; }
        public string BaseUrl { get; set; }
        public List<SensorInfo> Sensors { get; set; }
    }
    class SensorData
    {
        public string IPAddress { get; set; }
        public string data { get; set; }
    }
}