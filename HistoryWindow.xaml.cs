using System;
using System.IO;
using System.Windows;
using LiveTranslatorOverlay.Core.Localization;

namespace LiveTranslatorOverlay
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
            ApplyLocalization();
            LocalizationManager.Instance.LanguageChanged += (s, e) => Dispatcher.Invoke(ApplyLocalization);
        }

        public HistoryWindow(string initialHistory) : this()
        {
            if (!string.IsNullOrEmpty(initialHistory))
            {
                TxtHistory.Text = initialHistory;
                TxtHistory.ScrollToEnd();
            }
        }

        public void ApplyLocalization()
        {
            var loc = LocalizationManager.Instance;
            this.Title = loc.Get("HistoryTitle");
            if (BtnSummarize != null) BtnSummarize.Content = loc.Get("BtnSummarize");
            if (BtnClear != null) BtnClear.Content = loc.Get("BtnClear");
            if (BtnSave != null) BtnSave.Content = loc.Get("BtnSave");
        }

        public void AppendText(string sourceText, string translatedText)
        {
            Dispatcher.Invoke(() =>
            {
                string entry = $"[{DateTime.Now:HH:mm:ss}]\r\nOriginal: {sourceText}\r\nTranslated: {translatedText}\r\n----------------------------------------\r\n\r\n";
                TxtHistory.AppendText(entry);
                TxtHistory.ScrollToEnd();
            });
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtHistory.Clear();
            if (Application.Current?.MainWindow is MainWindow mainWin)
            {
                mainWin.ClearHistoryBuffer();
            }
        }

        private async void BtnSummarize_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationManager.Instance;
            if (string.IsNullOrWhiteSpace(TxtHistory.Text))
            {
                MessageBox.Show(loc.Get("MsgEmptyHistory"), loc.Get("TitleEmpty"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string apiKey = "";
                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    apiKey = mainWin.TxtApiKey.Text;
                }

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    MessageBox.Show(loc.Get("MsgMissingApiKey"), loc.Get("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string conversation = TxtHistory.Text;
                if (conversation.Length > 15000)
                    conversation = conversation.Substring(conversation.Length - 15000);

                var loadingWindow = new Window()
                {
                    Title = loc.Get("WindowSummarizingTitle"),
                    Width = 300,
                    Height = 100,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Content = new System.Windows.Controls.TextBlock() 
                    { 
                        Text = loc.Get("TxtSummarizing"), 
                        TextAlignment = TextAlignment.Center, 
                        VerticalAlignment = VerticalAlignment.Center 
                    }
                };
                loadingWindow.Show();

                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                string systemPrompt = loc.Get("AiSummaryPrompt");

                var tempProvider = new LiveTranslatorOverlay.Core.Translation.GroqTranslateProvider(apiKey);
                string bestModel = await tempProvider.GetBestAvailableModelAsync();

                var payload = new
                {
                    model = bestModel, 
                    messages = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = conversation }
                    },
                    temperature = 0.3,
                    max_tokens = 1024
                };

                request.Content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

                var response = await client.SendAsync(request);
                loadingWindow.Close();

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var choices = doc.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0)
                    {
                        var content = choices[0].GetProperty("message").GetProperty("content").GetString() ?? "";
                        
                        if (content.Contains("<think>"))
                        {
                            int endIdx = content.IndexOf("</think>");
                            if (endIdx != -1) content = content.Substring(endIdx + 8).Trim();
                        }
                        
                        var summaryWindow = new Window
                        {
                            Title = loc.Get("WindowSummaryTitle"),
                            Width = 700,
                            Height = 600,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            Owner = this
                        };
                        
                        var txtOut = new System.Windows.Controls.TextBox
                        {
                            Text = content,
                            TextWrapping = TextWrapping.Wrap,
                            AcceptsReturn = true,
                            Margin = new Thickness(10),
                            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                            FontSize = 14,
                            IsReadOnly = true,
                            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
                        };
                        
                        summaryWindow.Content = txtOut;
                        summaryWindow.Show();
                    }
                }
                else
                {
                    string err = await response.Content.ReadAsStringAsync();
                    MessageBox.Show($"API Error: {response.StatusCode}\n{err}", loc.Get("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{loc.Get("TitleError")}: {ex.Message}", loc.Get("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var loc = LocalizationManager.Instance;
            try
            {
                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string fileName = $"LiveTranslator_History_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string fullPath = Path.Combine(docsPath, fileName);
                
                File.WriteAllText(fullPath, TxtHistory.Text);
                MessageBox.Show(loc.Get("MsgSavedSuccess", fullPath), loc.Get("TitleSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(loc.Get("MsgSaveFailed", ex.Message), loc.Get("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
