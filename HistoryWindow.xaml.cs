using System;
using System.IO;
using System.Windows;

namespace LiveTranslatorOverlay
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
            this.SourceInitialized += HistoryWindow_SourceInitialized;
        }

        private void HistoryWindow_SourceInitialized(object? sender, EventArgs e)
        {
            LiveTranslatorOverlay.Core.CaptureExclusion.ExcludeFromCapture(this);
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
        }

        private async void BtnSummarize_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtHistory.Text))
            {
                MessageBox.Show("Belum ada history percakapan untuk dirangkum.", "Kosong", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Ambil API Key dari MainWindow
                string apiKey = "";
                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    apiKey = mainWin.TxtApiKey.Text;
                }

                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    MessageBox.Show("API Key Groq belum diisi di menu utama.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string conversation = TxtHistory.Text;
                
                // Jika terlalu panjang, batasi ke 15000 karakter terakhir agar tidak kena limit
                if (conversation.Length > 15000)
                    conversation = conversation.Substring(conversation.Length - 15000);

                var loadingWindow = new Window()
                {
                    Title = "AI Sedang Merangkum...",
                    Width = 300,
                    Height = 100,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    Content = new System.Windows.Controls.TextBlock() 
                    { 
                        Text = "Sedang menyusun Notulen Meeting...\nHarap tunggu sebentar.", 
                        TextAlignment = TextAlignment.Center, 
                        VerticalAlignment = VerticalAlignment.Center 
                    }
                };
                loadingWindow.SourceInitialized += (s, ev) => LiveTranslatorOverlay.Core.CaptureExclusion.ExcludeFromCapture(loadingWindow);
                loadingWindow.Show();

                using var client = new System.Net.Http.HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://api.groq.com/openai/v1/chat/completions");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

                string systemPrompt = "Kamu adalah AI Asisten Meeting profesional (seperti Fireflies.ai). Berikut adalah transkrip mentah dari rapat/percakapan (mungkin mengandung typo atau kalimat terpotong dari AI Speech-to-Text). Tugasmu: 1. Buat Ringkasan Eksekutif singkat. 2. Tulis poin-poin penting (Key Takeaways). 3. Tulis tindakan lanjutan (Action Items) jika ada. Gunakan bahasa Indonesia yang rapi, profesional, dan mudah dibaca.";

                // Cari model terbaik yang tersedia (agar tidak error NotFound)
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
                        
                        // Tampilkan di Window baru
                        var summaryWindow = new Window
                        {
                            Title = "Notulen Meeting (AI Summary)",
                            Width = 700,
                            Height = 600,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                            Owner = this
                        };
                        summaryWindow.SourceInitialized += (s, ev) => LiveTranslatorOverlay.Core.CaptureExclusion.ExcludeFromCapture(summaryWindow);
                        
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
                    MessageBox.Show($"Error API: {response.StatusCode}\n{err}", "Gagal Merangkum", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Terjadi kesalahan: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string docsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string fileName = $"LiveTranslator_History_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                string fullPath = Path.Combine(docsPath, fileName);
                
                File.WriteAllText(fullPath, TxtHistory.Text);
                MessageBox.Show($"Disimpan ke:\n{fullPath}", "Berhasil", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Gagal menyimpan: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Just hide it instead of closing so we keep the history
            e.Cancel = true;
            this.Hide();
        }
    }
}
