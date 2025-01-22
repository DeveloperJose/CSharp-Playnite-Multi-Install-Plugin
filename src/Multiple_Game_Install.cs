using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using CommonPluginsShared.PlayniteExtended;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using SuccessStory;
using SuccessStory.Services;

namespace Multiple_Game_Install
{
    public class Multiple_Game_Install : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("51a2784a-3dec-442c-841f-ad2cfab92363");

        /// <summary>
        /// A string used for window titles and other displays
        /// </summary>
        private readonly string Title = "Multiple Game Install Plugin";

        /// <summary>
        /// Games that the user selected to install that are currently not installed.
        /// </summary>
        private Game[] selectedNotInstalledGames;

        /// <summary>
        /// All games that the user selected to install. Can be already installed or not.
        /// </summary>
        private List<Game> selectedGames;

        /// <summary>
        /// How many games the user selected to install.
        /// </summary>
        private int selectedCount;

        /// <summary>
        /// How many games the user selected to install that are currently not installed.
        /// </summary>
        private int notInstalledCount;

        /// <summary>
        /// We listen to the game installation events and add those games here as a fallback to prevent infinite loops later.
        /// </summary>
        private HashSet<Guid> installedGamesFromCallback = new HashSet<Guid>();

        /// <summary>
        /// A reference to the SuccessStory plugin, if such plugin is installed by the user.
        /// </summary>
        private SuccessStory.SuccessStory successStoryPlugin;
        private bool successStory_AutoImport;
        private bool successStory_AutoImportOnInstalled;

        public Multiple_Game_Install(IPlayniteAPI api)
            : base(api)
        {
            Properties = new GenericPluginProperties { HasSettings = false };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            this.selectedGames = args.Games;
            this.selectedCount = selectedGames.Count();

            this.selectedNotInstalledGames = selectedGames
                .Where(game => !game.IsInstalled)
                .ToArray();
            this.notInstalledCount = selectedNotInstalledGames.Count();

            // The selection has no games that need installation or it has a single one for which we don't need this plugin
            if (notInstalledCount == 0)
                yield break;
            if (notInstalledCount == 1 && selectedCount == 1)
                yield break;

            yield return new GameMenuItem()
            {
                Description = $"Install {selectedCount} Games",
                Icon = "InstallIcon",
                Action = OnGameMenuClick,
            };
        }

        /// <summary>
        /// Handlers for when the user clicks the Install Games option from the Game Menu.
        /// </summary>
        /// <param name="args">Arguments passed by the menu item specifying the action details.</param>
        private void OnGameMenuClick(GameMenuItemActionArgs args)
        {
            // Check if SuccessStory is installed with "Automatically import for installed games" enabled
            if (successStoryPlugin == null)
                TryToGetSuccessStoryPlugin();

            if (successStoryPlugin != null)
                OverrideSuccessStorySettings();

            var selection = PlayniteApi.Dialogs.ShowMessage(
                $"Out of {selectedCount} selected games {notInstalledCount} are not installed. Proceed to install them?\n"
                    + "Please keep in mind that while games are being installed the UI will be a little laggy.",
                Title,
                System.Windows.MessageBoxButton.YesNo
            );

            if (selection == System.Windows.MessageBoxResult.Yes)
            {
                var options = new GlobalProgressOptions(Title)
                {
                    IsIndeterminate = false,
                    Cancelable = true,
                };

                PlayniteApi.Dialogs.ActivateGlobalProgress(InstallGamesGlobalProgress, options);
            }
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            base.OnGameInstalled(args);
            installedGamesFromCallback.Add(args.Game.Id);
        }

        /// <summary>
        /// Installs the selected games that are currently not installed.
        /// </summary>
        /// <param name="args">Arguments passed by the ActivateGlobalProgress event, used to update the current and max progress values.</param>
        private void InstallGamesGlobalProgress(GlobalProgressActionArgs args)
        {
            args.ProgressMaxValue = notInstalledCount;
            try
            {
                foreach (var currentGame in selectedNotInstalledGames)
                {
                    if (args.CancelToken.IsCancellationRequested)
                    {
                        logger.Info("User canceled multi-game installation.");
                        break;
                    }

                    args.Text =
                        $"Installing {currentGame}, {args.CurrentProgressValue}/{args.ProgressMaxValue}";
                    logger.Info(args.Text + ", " + currentGame.IsInstalled);

                    Task.Factory.StartNew(() =>
                    {
                        if (SynchronizationContext.Current == null)
                        {
                            var syncContext = new DispatcherSynchronizationContext(
                                PlayniteApi.MainView.UIDispatcher
                            );
                            SynchronizationContext.SetSynchronizationContext(syncContext);
                        }
                        PlayniteApi.InstallGame(currentGame.Id);
                    });

                    var startTime = DateTime.Now;
                    // Wait for the game to be installed or a minute to pass
                    while (!currentGame.IsInstalled)
                    {
                        Thread.Sleep(1000);
                        if (args.CancelToken.IsCancellationRequested || currentGame.IsInstalled)
                            break;

                        if (DateTime.Now - startTime > TimeSpan.FromMinutes(1))
                            break;
                    }

                    logger.Info(
                        $"Finished installing {currentGame}, installed={currentGame.IsInstalled}, or {installedGamesFromCallback.Contains(currentGame.Id)}"
                    );
                    args.CurrentProgressValue++;
                }
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException || ex is OperationCanceledException)
                    logger.Info("User canceled multi-game installation.");
                else
                    PlayniteApi.Dialogs.ShowErrorMessage(
                        $"Error while doing multi-game installation: {ex}"
                    );
            }
            finally
            {
                ResetSuccessStorySettings();
                installedGamesFromCallback.Clear();
            }
        }

        private void TryToGetSuccessStoryPlugin()
        {
            var plugins = PlayniteApi.Addons.Plugins;
            var possible = plugins.Find(plugin =>
                plugin.Id.ToString() == "cebe6d32-8c46-4459-b993-5a5189d60788"
            );
            if (possible != null)
            {
                successStoryPlugin = possible as SuccessStory.SuccessStory;
                logger.Info("Found SuccessStory plugin, will integrate into plugin.");
            }
        }

        private void OverrideSuccessStorySettings()
        {
            if (successStoryPlugin == null)
                return;

            // Make a copy
            successStory_AutoImport = successStoryPlugin.PluginSettings.Settings.AutoImport;
            successStory_AutoImportOnInstalled = successStoryPlugin
                .PluginSettings
                .Settings
                .AutoImportOnInstalled;

            logger.Info($"Overriding SuccessStory settings temporarily.");
            successStoryPlugin.PluginSettings.Settings.AutoImport = false;
            successStoryPlugin.PluginSettings.Settings.AutoImportOnInstalled = false;
        }

        private void ResetSuccessStorySettings()
        {
            if (successStoryPlugin == null)
                return;

            logger.Info($"Resetting SuccessStory settings back to their normal values.");
            successStoryPlugin.PluginSettings.Settings.AutoImport = successStory_AutoImport;
            successStoryPlugin.PluginSettings.Settings.AutoImportOnInstalled =
                successStory_AutoImportOnInstalled;

            logger.Info($"Telling SuccessStory to refresh database.");
            PluginExtended<
                SuccessStorySettingsViewModel,
                SuccessStoryDatabase
            >.PluginDatabase.Refresh(installedGamesFromCallback);
        }
    }
}
