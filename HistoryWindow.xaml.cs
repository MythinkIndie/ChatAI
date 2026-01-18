using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;

namespace ChatApp
{
    public partial class HistoryWindow : Window
    {
        private readonly ChatDbContext _dbContext;
        private readonly MainWindow _mainWindow;

        public HistoryWindow(ChatDbContext dbContext, MainWindow mainWindow)
        {
            InitializeComponent();
            _dbContext = dbContext;
            _mainWindow = mainWindow;
            LoadConversations();
        }

        private void LoadConversations(string searchTerm = "")
        {
            var query = _dbContext.Conversations
                .Include(c => c.Messages)
                .OrderByDescending(c => c.StartedAt)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(c => c.Title.Contains(searchTerm));
            }

            var conversations = query.ToList();

            ConversationsPanel.ItemsSource = conversations;
            TotalConversations.Text = conversations.Count.ToString();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            LoadConversations(SearchBox.Text);
        }

        private void LoadConversation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int conversationId)
            {
                _mainWindow.LoadConversation(conversationId);
                Close();
            }
        }

        private void DeleteConversation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is int conversationId)
            {
                var result = MessageBox.Show(
                    "¿Estás seguro de que deseas eliminar esta conversación? Esta acción no se puede deshacer.",
                    "Confirmar Eliminación",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var conversation = _dbContext.Conversations.Find(conversationId);
                    if (conversation != null)
                    {
                        _dbContext.Conversations.Remove(conversation);
                        _dbContext.SaveChanges();
                        LoadConversations(SearchBox.Text);
                    }
                }
            }
        }

        private void ConversationItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1F1F1F"));
            }
        }

        private void ConversationItem_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#171717"));
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}