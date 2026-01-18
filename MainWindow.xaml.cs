using Markdown.Xaml;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static System.Net.Mime.MediaTypeNames;

namespace ChatApp
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private readonly ChatDbContext _dbContext;
        private int _currentConversationId;
        private string _messageText = string.Empty;
        private bool _canSend = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<ChatMessage> Messages { get; set; }

        public string MessageText
        {
            get => _messageText;
            set
            {
                _messageText = value;
                OnPropertyChanged();
                CanSend = !string.IsNullOrWhiteSpace(value);
            }
        }

        public bool CanSend
        {
            get => _canSend;
            set
            {
                _canSend = value;
                OnPropertyChanged();
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            Messages = new ObservableCollection<ChatMessage>();
            ChatMessagesPanel.ItemsSource = Messages;

            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("http://localhost:5000/"),
                Timeout = TimeSpan.FromMinutes(5)
            };

            _dbContext = new ChatDbContext();
            try
            {
                _dbContext.Database.EnsureCreated();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }

            StartNewConversation();
        }

        private void StartNewConversation()
        {
            var conversation = new Conversation
            {
                StartedAt = DateTime.Now,
                Title = "Nueva conversación"
            };

            _dbContext.Conversations.Add(conversation);
            _dbContext.SaveChanges();

            _currentConversationId = conversation.Id;
            Messages.Clear();

            // Mensaje de bienvenida
            AddMessage("¡Hola! Soy tu asistente AI. ¿En qué puedo ayudarte hoy?", false);
        }

        private async void SendMessage_Click(object sender, RoutedEventArgs e)
        {
            await SendMessageAsync();
        }

        private async void MessageInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                e.Handled = true;
                await SendMessageAsync();
            }
        }

        private List<object> BuildConversationHistory(int conversationId)
        {
            return _dbContext.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new
                {
                    role = m.IsUserMessage ? "user" : "assistant",
                    content = m.Content
                })
                .ToList<object>();
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(MessageText) || !CanSend)
                return;

            var userMessage = MessageText.Trim();
            MessageText = string.Empty;
            CanSend = false;

            // Agregar mensaje del usuario a la UI
            AddMessage(userMessage, true);

            // Guardar mensaje del usuario en la base de datos
            SaveMessageToDb(userMessage, true);

            // Crear mensaje del asistente vacío para streaming
            var assistantMessage = new ChatMessage
            {
                RawText = "",
                IsUser = false,
                Role = "Asistente",
                Avatar = "AI",
                Timestamp = DateTime.Now.ToString("HH:mm")
            };

            Messages.Add(assistantMessage);

            try
            {
                // Hacer POST a la API con streaming
                var response = await SendToApiAsync(userMessage, assistantMessage);

                // Guardar respuesta completa en la base de datos
                SaveMessageToDb(response, false);

                // Actualizar título de la conversación si es el primer mensaje
                UpdateConversationTitle();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al enviar el mensaje: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                // Remover el mensaje del asistente si hubo error
                Messages.Remove(assistantMessage);
            }
            finally
            {
                CanSend = true;
            }

            // Auto-scroll al final
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(100);
                ChatScrollViewer.ScrollToEnd();
            });
        }

        private async Task<string> SendToApiAsync(string message, ChatMessage assistantMessage)
        {
            try
            {
                var messages = BuildConversationHistory(_currentConversationId);
                messages.Add(new { role = "user", content = message });
                var requestData = new { messages };

                var json = JsonSerializer.Serialize(requestData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:7452/chat")
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead
                );

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var fullResponse = new StringBuilder();
                string detectedService = "unknown";

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var jsonData = JsonDocument.Parse(line);

                        // Extraer metadata
                        if (jsonData.RootElement.TryGetProperty("service", out var serviceElement))
                        {
                            detectedService = serviceElement.GetString();
                        }

                        // Extraer contenido
                        if (jsonData.RootElement.TryGetProperty("content", out var contentElement))
                        {
                            var chunk = contentElement.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                fullResponse.Append(chunk);

                                // ✅ APLICAR FORMATEO ESPECÍFICO DEL SERVICIO
                                var formattedChunk = AIResponseFormatter.FormatResponse(chunk, detectedService);

                                await Dispatcher.InvokeAsync(() =>
                                {
                                    assistantMessage.RawText = fullResponse.ToString();
                                    ChatScrollViewer.ScrollToEnd();
                                });
                            }
                        }

                        // Verificar si es el chunk final
                        if (jsonData.RootElement.TryGetProperty("isComplete", out var completeElement) &&
                            completeElement.GetBoolean())
                        {
                            break;
                        }
                    }
                    catch (JsonException)
                    {
                        // Si no es JSON, asumir que es texto plano del chunk anterior
                        fullResponse.Append(line);
                    }

                    // Pequeño delay para suavizar la animación
                    await Task.Delay(10);
                }

                var finalText = AIResponseFormatter.FormatResponse(
                    NormalizeAssistantMarkdown(fullResponse.ToString()),
                    detectedService
                );

                assistantMessage.RawText = finalText;

                return finalText;
            }
            catch (Exception ex)
            {
                throw new Exception("Error comunicándose con la API local", ex);
            }
        }

        private static string PreprocessMarkdownIssues(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // 1. Corregir problemas con bloques de código mal formateados
            // Patrón: "js" en una línea seguido de código (sin backticks)
            text = Regex.Replace(text, @"^(js|javascript)\s*\n([\s\S]*?)(?=\n\n|$)",
                "```javascript\n$2```", RegexOptions.Multiline);

            // 2. Corregir tablas mal formateadas (con números pegados)
            text = Regex.Replace(text, @"\|\s*\*\*([^*]+)\*\*\s*(\d+)\.\s*\|",
                "| **$1** |", RegexOptions.Multiline);

            // 3. Asegurar que las tablas tengan formato correcto
            text = Regex.Replace(text, @"\|\s*:\s*---\s*\|",
                "| :--- |", RegexOptions.Multiline);

            // 4. Corregir código que empieza sin backticks pero debería tenerlos
            text = Regex.Replace(text, @"^(\s*)(while|for|if|function|const|let|var)\s*\([^)]*\)\s*\{[\s\S]*?\n\1\}(?!`)",
                "```javascript\n$0```", RegexOptions.Multiline);

            // 5. Arreglar template strings mal interpretados
            text = Regex.Replace(text, @"console\.log\(Dijiste:\s*(\$\{[^}]+\})\);",
                "console.log(`Dijiste: ${$1}`);");

            // 6. Corregir números pegados a texto en tablas
            text = Regex.Replace(text, @"(\|[^\n|]+\|)\s*(\d+)\.\s*", "$1 ", RegexOptions.Multiline);

            return text;
        }

        // Luego modifica NormalizeAssistantMarkdown:
        private static string NormalizeAssistantMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            // Preprocesar problemas comunes primero
            text = PreprocessMarkdownIssues(text);

            var sb = new StringBuilder();

            // Procesar por líneas para mejor control
            var lines = text.Split('\n');
            bool inCodeBlock = false;
            string currentLanguage = "";
            bool inTable = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmedLine = line.Trim();

                // Detectar inicio de bloque de código
                if (trimmedLine.StartsWith("```") && !inCodeBlock)
                {
                    inCodeBlock = true;
                    currentLanguage = trimmedLine.Length > 3 ? trimmedLine.Substring(3).Trim() : "";
                    sb.AppendLine(line);
                    continue;
                }

                // Detectar fin de bloque de código
                if (trimmedLine.StartsWith("```") && inCodeBlock)
                {
                    inCodeBlock = false;
                    currentLanguage = "";
                    sb.AppendLine(line);
                    continue;
                }

                // Detectar tablas
                if (!inCodeBlock && line.Contains("|") && line.Contains("---"))
                {
                    inTable = true;
                }
                else if (inTable && !line.Contains("|"))
                {
                    inTable = false;
                }

                // Si estamos en bloque de código, mantener tal cual
                if (inCodeBlock)
                {
                    sb.AppendLine(line);
                    continue;
                }

                // Procesar líneas normales (no código)

                // 1. Arreglar títulos que no tienen espacio después del #
                if (Regex.IsMatch(trimmedLine, @"^#{1,6}[^#\s]"))
                {
                    line = Regex.Replace(line, @"^(#{1,6})([^#\s])", "$1 $2");
                }

                // 2. Arreglar listas
                if (Regex.IsMatch(trimmedLine, @"^[-*•]\s+") && i > 0 && !string.IsNullOrWhiteSpace(lines[i - 1]))
                {
                    sb.AppendLine(); // Añadir línea en blanco antes de lista
                }

                // 3. Procesar tablas
                if (inTable)
                {
                    // Limpiar números pegados en celdas de tabla
                    line = Regex.Replace(line, @"\|\s*(\d+)\.\s*", "| ");

                    // Asegurar formato de separador de tabla
                    if (line.Contains("---"))
                    {
                        line = line.Replace(":---", " :--- ").Replace("---:", " ---: ").Replace("---", " --- ");
                    }

                    // Añadir espacios alrededor de las celdas para mejor legibilidad
                    line = Regex.Replace(line, @"\|\s*", "| ");
                    line = Regex.Replace(line, @"\s*\|", " |");
                }

                // 4. Eliminar backticks sueltos que no son parte de bloques de código
                if (!inCodeBlock && line.Contains("`") && !line.Contains("```"))
                {
                    // Mantener backticks inline pero asegurar que no estén rotos
                    line = line.Replace(" `` ", " `").Replace("` ", "`").Replace(" `", "`");
                }

                sb.AppendLine(line);
            }

            var result = sb.ToString();

            // Limpiar final
            result = result.Trim();

            // Eliminar múltiples líneas en blanco consecutivas
            result = Regex.Replace(result, @"\n{3,}", "\n\n");

            return result;
        }

        private static string FormatTableNicely(List<string[]> rows)
        {
            if (rows.Count < 2) return string.Join("\n", rows.Select(r => "| " + string.Join(" | ", r) + " |"));

            var result = new StringBuilder();

            // Calcular anchos máximos por columna
            int colCount = rows[0].Length;
            int[] colWidths = new int[colCount];

            foreach (var row in rows)
            {
                for (int i = 0; i < colCount && i < row.Length; i++)
                {
                    int length = CleanTableCell(row[i]).Length;
                    if (length > colWidths[i])
                        colWidths[i] = length;
                }
            }

            // Construir la tabla formateada
            for (int rowIdx = 0; rowIdx < rows.Count; rowIdx++)
            {
                var row = rows[rowIdx];
                var formattedCells = new List<string>();

                for (int colIdx = 0; colIdx < colCount; colIdx++)
                {
                    string cell = colIdx < row.Length ? row[colIdx] : "";
                    string cleanCell = CleanTableCell(cell);

                    // Determinar alineación (basado en la segunda fila, que es el separador)
                    if (rowIdx == 1 && cleanCell.StartsWith(":") && cleanCell.EndsWith(":"))
                    {
                        // Centrado
                        formattedCells.Add(cleanCell.PadLeft((colWidths[colIdx] - cleanCell.Length) / 2 + cleanCell.Length).PadRight(colWidths[colIdx]));
                    }
                    else if (rowIdx == 1 && cleanCell.EndsWith(":"))
                    {
                        // Derecha
                        formattedCells.Add(cleanCell.PadLeft(colWidths[colIdx]));
                    }
                    else if (rowIdx == 1 && cleanCell.StartsWith(":"))
                    {
                        // Izquierda (ya está)
                        formattedCells.Add(cleanCell.PadRight(colWidths[colIdx]));
                    }
                    else
                    {
                        // Contenido normal
                        formattedCells.Add(cleanCell.PadRight(colWidths[colIdx]));
                    }
                }

                result.AppendLine("| " + string.Join(" | ", formattedCells) + " |");
            }

            return result.ToString();
        }

        private static string CleanTableCell(string cell)
        {
            // Limpiar números pegados, asteriscos extra, etc.
            string cleaned = cell.Trim();

            // Remover números pegados al final (como "18.", "19.", etc.)
            cleaned = Regex.Replace(cleaned, @"\s*\d+\.\s*$", "");

            // Limpiar formato markdown excesivo
            cleaned = cleaned.Replace("**", "").Replace("__", "");

            return cleaned.Trim();
        }

        private void AddMessage(string content, bool isUser)
        {
            var message = new ChatMessage
            {
                RawText = content,
                IsUser = isUser,
                Role = isUser ? "Tú" : "Asistente",
                Avatar = isUser ? "U" : "AI",
                Timestamp = DateTime.Now.ToString("HH:mm")
            };

            Messages.Add(message);

            // Auto-scroll al final
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(100);
                ChatScrollViewer.ScrollToEnd();
            });
        }

        private void SaveMessageToDb(string content, bool isUser)
        {
            var message = new Message
            {
                ConversationId = _currentConversationId,
                Content = content,
                IsUserMessage = isUser,
                Timestamp = DateTime.Now,
                ApiResponse = string.Empty
            };

            _dbContext.Messages.Add(message);
            _dbContext.SaveChanges();
        }

        private void UpdateConversationTitle()
        {
            var conversation = _dbContext.Conversations.Find(_currentConversationId);
            if (conversation != null && conversation.Title == "Nueva conversación")
            {
                var firstMessage = _dbContext.Messages
                    .Where(m => m.ConversationId == _currentConversationId && m.IsUserMessage)
                    .OrderBy(m => m.Timestamp)
                    .FirstOrDefault();

                if (firstMessage != null)
                {
                    conversation.Title = firstMessage.Content.Length > 50
                        ? firstMessage.Content.Substring(0, 47) + "..."
                        : firstMessage.Content;

                    _dbContext.SaveChanges();
                }
            }
        }

        private void NewChat_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "¿Deseas iniciar una nueva conversación? La conversación actual se guardará.",
                "Nueva Conversación",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                StartNewConversation();
            }
        }

        private void History_Click(object sender, RoutedEventArgs e)
        {
            var historyWindow = new HistoryWindow(_dbContext, this);
            historyWindow.Owner = this;
            historyWindow.ShowDialog();
        }

        public void LoadConversation(int conversationId)
        {
            _currentConversationId = conversationId;
            Messages.Clear();

            var messages = _dbContext.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Timestamp)
                .ToList();

            foreach (var msg in messages)
            {
                AddMessage(msg.Content, msg.IsUserMessage);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _dbContext?.Dispose();
            _httpClient?.Dispose();
        }
    }

    public class ChatMessage : INotifyPropertyChanged
    {
        private string _rawText = "";
        private FlowDocument _document;

        public string RawText
        {
            get => _rawText;
            set
            {
                _rawText = value;
                OnPropertyChanged();
                UpdateDocument();
            }
        }

        public FlowDocument Document
        {
            get => _document;
            private set
            {
                _document = value;
                OnPropertyChanged();
            }
        }

        public bool IsUser { get; set; }
        public string Role { get; set; }
        public string Avatar { get; set; }
        public string Timestamp { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void UpdateDocument()
        {

            var markdown = new Markdown.Xaml.Markdown();

            Document = markdown.Transform(RawText ?? "");
            
        }

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        
    }
    public static class RichTextBoxHelper
    {
        public static readonly DependencyProperty DocumentProperty =
            DependencyProperty.RegisterAttached(
                "Document",
                typeof(FlowDocument),
                typeof(RichTextBoxHelper),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDocumentChanged));

        public static void SetDocument(DependencyObject element, FlowDocument value) =>
            element.SetValue(DocumentProperty, value);

        public static FlowDocument GetDocument(DependencyObject element) =>
            (FlowDocument)element.GetValue(DocumentProperty);

        private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not RichTextBox rtb) return;

            // Evita asignaciones innecesarias
            var newDoc = e.NewValue as FlowDocument;
            if (rtb.Document != newDoc)
            {
                rtb.Document = newDoc ?? new FlowDocument();
            }
        }
    }

    public static class AIResponseFormatter
    {
        // Formateador para Groq (generalmente mejor para inglés)
        public static string FormatGroqResponse(string rawText)
        {
            // Usar StringBuilder para reemplazos simples
            var formatted = new StringBuilder(rawText);

            // 1. Asegurar saltos de línea apropiados - CORREGIDO
            formatted.Replace("\n\n", "\n"); // Replace modifica el StringBuilder, no devuelve nada

            var text = formatted.ToString();

            // 2. Mejorar formato de listas para Groq
            text = Regex.Replace(text, @"^(\s*)[•\-*]\s+", "$1• ", RegexOptions.Multiline);

            // 3. Groq suele responder con mucho código, asegurar formato
            if (ContainsCodeBlock(text))
            {
                text = EnsureCodeBlockSpacing(text);
            }

            // 4. Groq es más conciso, añadir claridad si es muy breve - CORREGIDO
            if (text.Length < 100 && !text.Contains("\n"))
            {
                text += "\n\n---";
            }

            return text;
        }

        // Formateador para Cerebras GLM-4.6 (mejor para chino/inglés mixto)
        public static string FormatCerebrasResponse(string rawText)
        {
            // Cerebras GLM-4.6 tiene características específicas:
            var text = rawText;

            // 1. Tiende a usar títulos con ##
            text = Regex.Replace(text, @"^##\s+(.+)$", "## $1\n", RegexOptions.Multiline);

            // 2. Mejorar formato para contenido bilingüe
            text = EnsureBilingualFormatting(text);

            // 3. GLM-4.6 puede generar contenido muy estructurado
            text = EnhanceStructure(text);

            // 4. Añadir separadores para secciones largas
            text = AddSectionSeparators(text);

            return text;
        }

        // Formateador para Moonshot Kimi (especializado en chino)
        public static string FormatMoonshotKimiResponse(string rawText)
        {
            // Kimi es excelente con chino y tiene formato específico:
            var text = rawText;

            // 1. Preservar caracteres chinos correctamente
            text = PreserveChineseCharacters(text);

            // 2. Kimi usa mucho emoji y formato informal
            text = BalanceEmojiUsage(text);

            // 3. Asegurar que el markdown funcione con caracteres chinos
            text = FixChineseMarkdown(text);

            // 4. Kimi tiende a ser más conversacional
            text = MakeConversational(text);

            return text;
        }

        private static bool ContainsCodeBlock(string text)
        {
            return text.Contains("```") || text.Contains("    ") || text.Contains("\t");
        }

        private static string EnsureCodeBlockSpacing(string text)
        {
            return Regex.Replace(text, @"(?<!\n)```", "\n```");
        }

        private static string EnsureBilingualFormatting(string text)
        {
            // Para contenido chino/inglés mixto
            return Regex.Replace(text, @"([\u4e00-\u9fff])([A-Za-z])", "$1 $2");
        }

        private static string EnhanceStructure(string text)
        {
            // Añadir numeración automática a listas largas
            int counter = 1;
            return Regex.Replace(text, @"^(\s*)[•\-*]\s+",
                m => $"{m.Groups[1].Value}{counter++}. ",
                RegexOptions.Multiline);
        }

        private static string AddSectionSeparators(string text)
        {
            var lines = text.Split('\n');
            var result = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                result.AppendLine(lines[i]);

                // Añadir separador después de títulos
                if (Regex.IsMatch(lines[i], @"^#{1,3}\s+.+$") &&
                    i < lines.Length - 1 &&
                    !string.IsNullOrWhiteSpace(lines[i + 1]))
                {
                    result.AppendLine("---");
                }
            }

            return result.ToString();
        }

        private static string PreserveChineseCharacters(string text)
        {
            // Asegurar que los caracteres chinos tengan espacio apropiado
            return Regex.Replace(text, @"([\u4e00-\u9fff])([,.!?])", "$1$2 ");
        }

        private static string BalanceEmojiUsage(string text)
        {
            // Limitar emojis excesivos pero mantener algunos
            var emojiCount = Regex.Matches(text, @"[\u263a-\u1f9ff]").Count;
            if (emojiCount > 5)
            {
                // Reducir emojis si hay demasiados
                text = Regex.Replace(text, @"([\u263a-\u1f9ff]){2,}", "$1");
            }
            return text;
        }

        private static string FixChineseMarkdown(string text)
        {
            // Arreglar markdown con caracteres chinos
            return Regex.Replace(text, @"([\u4e00-\u9fff])`([^`]+)`", "$1 `$2`");
        }

        private static string MakeConversational(string text)
        {
            // Añadir toque conversacional si es muy formal
            if (!text.Contains("!") && !text.Contains("?") && text.Length > 50)
            {
                text = text.TrimEnd() + "\n\n¿Te ha quedado claro?";
            }
            return text;
        }

        // Función principal que detecta y aplica el formateador correcto
        public static string FormatResponse(string rawText, string service)
        {
            if (string.IsNullOrEmpty(service))
                return rawText;

            return service.ToLower() switch
            {
                "groq" => FormatGroqResponse(rawText),
                "cerebras" => FormatCerebrasResponse(rawText),
                "moonshot" => FormatMoonshotKimiResponse(rawText),
                _ => rawText // Sin formateo si no se reconoce
            };
        }
    }

    public class ResponsivePaddingConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Window window)
            {
                double width = window.ActualWidth;

                if (width < 700) return new Thickness(10, 10, 10, 10);
                if (width < 900) return new Thickness(20, 15, 20, 15);
                if (width < 1200) return new Thickness(30, 20, 30, 20);
                return new Thickness(40, 20, 40, 20);
            }

            if (value is ScrollViewer scrollViewer)
            {
                double width = scrollViewer.ActualWidth;

                if (width < 700) return new Thickness(10, 10, 10, 10);
                if (width < 900) return new Thickness(20, 15, 20, 15);
                if (width < 1200) return new Thickness(30, 20, 30, 20);
                return new Thickness(40, 20, 40, 20);
            }

            return new Thickness(20);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}