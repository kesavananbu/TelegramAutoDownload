using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace TelegramAutoDownload.UITests
{
    /// <summary>
    /// UI automation tests for the main application window.
    ///
    /// These tests launch the real WPF executable and interact with it via the
    /// Windows UI Automation (UIA3) framework. They verify high-level UI behaviour
    /// without depending on Telegram credentials or network connectivity.
    ///
    /// Run with:
    ///   dotnet test TelegramAutoDownload.UITests
    ///
    /// Skip automatically in headless CI:
    ///   Set environment variable SKIP_UI_TESTS=1 before running.
    ///   The [UIFact] attribute will mark tests as Skipped in that case.
    /// </summary>
    [Collection("UI")]   // Serialise all UI tests — only one app instance at a time
    public class MainWindowTests : UITestBase
    {
        // ---------------------------------------------------------------------------
        // Window baseline
        // ---------------------------------------------------------------------------

        [UIFact]
        public void MainWindow_Opens_WithCorrectTitle()
        {
            MainWindow.Should().NotBeNull("the main window must be visible after launch");
            MainWindow.Title.Should().Contain("Telegram",
                because: "the window title must identify the application");
        }

        [UIFact]
        public void MainWindow_IsNotMinimised_OnStartup()
        {
            MainWindow.IsOffscreen.Should().BeFalse(
                "the window must be visible (not minimised) on first launch");
        }

        // ---------------------------------------------------------------------------
        // Chat list
        // ---------------------------------------------------------------------------

        [UIFact]
        public void ChatListView_IsPresent()
        {
            // The chat list is a ListView
            var listView = MainWindow.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.List))?.AsListBox();

            listView.Should().NotBeNull(
                "a list control for monitored chats must exist in the main window");
        }

        // ---------------------------------------------------------------------------
        // Search / filter bar
        // ---------------------------------------------------------------------------

        [UIFact]
        public void SearchTextBox_IsPresent()
        {
            var textBoxes = MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
            textBoxes.Should().NotBeEmpty("at least one text input (search/filter) must exist in the main window");
        }

        // ---------------------------------------------------------------------------
        // Downloads tab / panel
        // ---------------------------------------------------------------------------

        [UIFact]
        public void DownloadsGrid_IsPresent()
        {
            // The downloads progress grid is a DataGrid
            var grid = MainWindow.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.DataGrid));

            grid.Should().NotBeNull(
                "a DataGrid for download progress must be visible in the main window");
        }

        // ---------------------------------------------------------------------------
        // Sync button
        // ---------------------------------------------------------------------------

        [UIFact]
        public void SyncButton_IsPresent_AndEnabled()
        {
            var byId = MainWindow.FindFirstDescendant(cf =>
                cf.ByAutomationId("ChatHistorySyncButton"))?.AsButton();
            if (byId != null)
            {
                byId.IsEnabled.Should().BeTrue("the Sync button must be enabled when visible");
                return;
            }

            var buttons = MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            var syncButton = buttons.FirstOrDefault(b =>
                b.Name?.Contains("Sync", StringComparison.OrdinalIgnoreCase) == true ||
                b.AutomationId?.Contains("Sync", StringComparison.OrdinalIgnoreCase) == true);

            syncButton.Should().NotBeNull("a Sync button must exist in the main window (add a monitored chat if the list is empty)");
            syncButton!.IsEnabled.Should().BeTrue("the Sync button must be enabled when the app starts");
        }

        // ---------------------------------------------------------------------------
        // Settings window
        // ---------------------------------------------------------------------------

        [UIFact]
        public void SettingsButton_Click_OpensSettingsWindow()
        {
            var buttons = MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            var settingsButton = buttons.FirstOrDefault(b =>
                b.Name?.Contains("Setting", StringComparison.OrdinalIgnoreCase) == true ||
                b.AutomationId?.Contains("Setting", StringComparison.OrdinalIgnoreCase) == true);

            settingsButton.Should().NotBeNull("a Settings button must exist");
            settingsButton!.AsButton().Click();

            // Modal / slow machines: poll for the settings window instead of assuming an extra top-level count.
            Window? settingsWindow = null;
            for (var i = 0; i < 40; i++)
            {
                settingsWindow = App.GetAllTopLevelWindows(Automation).FirstOrDefault(w =>
                    w.Title?.Contains("Settings", StringComparison.OrdinalIgnoreCase) == true);
                if (settingsWindow != null) break;
                Thread.Sleep(250);
            }

            settingsWindow.Should().NotBeNull("clicking Settings must open the Settings window");
            try { settingsWindow!.Close(); } catch { /* ignore if already closed */ }
        }

    }
}
