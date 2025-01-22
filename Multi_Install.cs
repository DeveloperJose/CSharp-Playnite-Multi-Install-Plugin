using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace Multi_Install
{
    public class Multi_Install : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        public override Guid Id { get; } = Guid.Parse("51a2784a-3dec-442c-841f-ad2cfab92363");

        /// <summary>
        /// A string used for window titles and other displays
        /// </summary>
        private readonly string Title = "Multi-Install Plugin";

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

        public Multi_Install(IPlayniteAPI api)
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
            var selection = PlayniteApi.Dialogs.ShowMessage(
                $"Out of {selectedCount} selected games {notInstalledCount} are not installed. Proceed to install them?",
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

                // Run this in the UI thread so InvokeInstallation and such other calls work properly
                PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                {
                    PlayniteApi.Dialogs.ActivateGlobalProgress(InstallGamesGlobalProgress, options);
                });
            }
        }

        /// <summary>
        /// Installs the selected games that are currently not installed.
        /// </summary>
        /// <param name="args">Arguments passed by the ActivateGlobalProgress event, used to update the current and max progress values.</param>
        private void InstallGamesGlobalProgress(GlobalProgressActionArgs args)
        {
            args.ProgressMaxValue = notInstalledCount;

            foreach (var currentGame in selectedNotInstalledGames)
            {
                args.Text =
                    $"Installing {currentGame}, {args.CurrentProgressValue}/{args.ProgressMaxValue}";
                logger.Info(args.Text);

                PlayniteApi.InstallGame(currentGame.Id);
                args.CurrentProgressValue++;
            }
        }
    }
}
