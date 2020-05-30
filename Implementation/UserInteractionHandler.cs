using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Xna.Framework;
using OTAPI.Tile;
using Terraria.ID;
using DPoint = System.Drawing.Point;

using Terraria.Plugins.Common;

using TShockAPI;
using TShockAPI.DB;

namespace Terraria.Plugins.CoderCow.HouseRegions
{
    public class UserInteractionHandler : UserInteractionHandlerBase, IDisposable
    {
        #region [Nested: CommandExecDummyPlayer]
        private class CommandExecDummyPlayer : TSPlayer
        {
            private readonly Action<string, Color> sendMessageHandler;

            public CommandExecDummyPlayer(
              TSPlayer originalPlayer, Action<string, Color> sendMessageHandler
            ) : base(originalPlayer.Name)
            {
                this.User.ID = originalPlayer.User.ID;
                this.User.Name = originalPlayer.User.Name;
                this.IsLoggedIn = originalPlayer.IsLoggedIn;
                this.Group = originalPlayer.Group;

                this.sendMessageHandler = sendMessageHandler;
            }

            public override void SendMessage(string msg, Color color)
            {
                this.sendMessageHandler?.Invoke(msg, color);
            }
        }
        #endregion

        protected PluginInfo PluginInfo { get; private set; }
        protected Configuration Config { get; private set; }
        protected HousingManager HousingManager { get; private set; }
        protected Func<Configuration> ReloadConfigurationCallback { get; private set; }


        public UserInteractionHandler(
          PluginTrace trace, PluginInfo pluginInfo, Configuration config, HousingManager housingManager,
          Func<Configuration> reloadConfigurationCallback
        ) : base(trace)
        {
            if (trace == null) throw new ArgumentNullException();
            if (config == null) throw new ArgumentNullException();
            if (housingManager == null) throw new ArgumentNullException();
            if (reloadConfigurationCallback == null) throw new ArgumentNullException();
            if (pluginInfo.Equals(PluginInfo.Empty)) throw new ArgumentException();

            this.PluginInfo = pluginInfo;
            this.Config = config;
            this.HousingManager = housingManager;
            this.ReloadConfigurationCallback = reloadConfigurationCallback;

            #region Command Setup
            base.RegisterCommand(
              new[] { "lote", "loteamento", "house", "housing" }, this.RootCommand_Exec, this.RootCommand_HelpCallback
            );
            #endregion
        }

        #region [Command Handling /lote]
        private void RootCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            base.StopInteraction(args.Player);

            if (args.Parameters.Count >= 1)
            {
                string subCommand = args.Parameters[0].ToLowerInvariant();

                if (this.TryExecuteSubCommand(subCommand, args))
                    return;
            }

            //args.Player.SendMessage(this.PluginInfo.ToString(), Color.White);
            //args.Player.SendMessage(this.PluginInfo.Description, Color.White);
            //args.Player.SendMessage(string.Empty, Color.Yellow);

            int playerHouseCount = 0;
            for (int i = 0; i < TShock.Regions.Regions.Count; i++)
            {
                string houseOwner;
                int houseIndex;
                if (
                  this.HousingManager.TryGetHouseRegionData(TShock.Regions.Regions[i].Name, out houseOwner, out houseIndex) &&
                  houseOwner == args.Player.User.Name
                )
                    playerHouseCount++;
            }

            string statsMessage = string.Format(
              "Você definiu {0} de {1} possíveis lotes até agora.", playerHouseCount, this.Config.MaxHousesPerUser
            );
            args.Player.SendMessage(statsMessage, Color.Magenta);
            args.Player.SendMessage("Digite [c/ffd700:\"/lote comandos\"] para ver os comandos disponíveis.", Color.ForestGreen);
            args.Player.SendMessage("Para ver informações gerais sobre qualquer comando digite [c/ffd700:\"/lote ][c/ff00ff:(comando)] [c/ffd700:ajuda]\".", Color.ForestGreen);
        }

        private bool TryExecuteSubCommand(string commandNameLC, CommandArgs args)
        {
            switch (commandNameLC)
            {
                case "commands":
                case "comandos":
                case "cmds":
                    {
                        int pageNumber;
                        if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pageNumber))
                            return true;

                        List<string> terms = new List<string>();
                        terms.Add("/lote info");
                        terms.Add("/lote scan");
                        if (args.Player.Group.HasPermission(HouseRegionsPlugin.HousingMaster_Permission))
                            terms.Add("/lote resumo");
                        if (args.Player.Group.HasPermission(HouseRegionsPlugin.Define_Permission))
                        {
                            terms.Add("/lote demarcar");
                            terms.Add("/lote redim");
                        }
                        if (args.Player.Group.HasPermission(HouseRegionsPlugin.Delete_Permission))
                            terms.Add("/lote deletar");
                        if (args.Player.Group.HasPermission(HouseRegionsPlugin.Share_Permission))
                        {
                            terms.Add("/lote novo-dono");
                            terms.Add("/lote compartilhar");
                            terms.Add("/lote descompartilhar");
                        }
                        if (args.Player.Group.HasPermission(HouseRegionsPlugin.ShareWithGroups_Permission))
                        {
                            terms.Add("/lote compartilhar-grupo");
                            terms.Add("/lote descompartilhar-grupo");
                        }
                        if (args.Player.Group.HasPermission(HouseRegionsPlugin.Cfg_Permission))
                            terms.Add("/lote reload");

                        List<string> lines = PaginationTools.BuildLinesFromTerms(terms);
                        PaginationTools.SendPage(args.Player, pageNumber, lines, new PaginationTools.Settings
                        {
                            HeaderFormat = "Comandos de Loteamento (Página {0} de {1})",
                            LineTextColor = Color.LightGray,
                        });

                        return true;
                    }
                case "resumo":
                case "summary":
                    this.HouseSummaryCommand_Exec(args);
                    return true;
                case "info":
                    this.HouseInfoCommand_Exec(args);
                    return true;
                case "scan":
                    this.HouseScanCommand_Exec(args);
                    return true;
                case "define":
                case "definir":
                case "demarcar":
                case "def":
                    if (!args.Player.Group.HasPermission(HouseRegionsPlugin.Define_Permission))
                    {
                        args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                        return true;
                    }

                    this.HouseDefineCommand_Exec(args);
                    return true;
                case "resize":
                case "redimensionar":
                case "redim":
                    if (!args.Player.Group.HasPermission(HouseRegionsPlugin.Define_Permission))
                    {
                        args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                        return true;
                    }

                    this.HouseResizeCommand_Exec(args);
                    return true;
                case "delete":
                case "del":
                case "deletar":
                    if (!args.Player.Group.HasPermission(HouseRegionsPlugin.Delete_Permission))
                    {
                        args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                        return true;
                    }

                    this.HouseDeleteCommand_Exec(args);
                    return true;
                case "setowner":
                case "definir-dono":
                case "novo-dono":
                    if (!args.Player.Group.HasPermission(HouseRegionsPlugin.Share_Permission))
                    {
                        args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                        return true;
                    }

                    this.HouseSetOwnerCommand_Exec(args);
                    return true;
                case "shareuser":
                case "share":
                case "compartilhar":
                case "compart":
                    if (!args.Player.Group.HasPermission(HouseRegionsPlugin.Share_Permission))
                    {
                        args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                        return true;
                    }

                    this.HouseShareCommand_Exec(args);
                    return true;
                case "unshareuser":
                case "descompart":
                case "descompartilhar":
                case "unshare":
                    if (!args.Player.Group.HasPermission(HouseRegionsPlugin.Share_Permission))
                    {
                        args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                        return true;
                    }

                    this.HouseUnshareCommand_Exec(args);
                    return true;
                case "sharegroup":
                case "shareg":
                case "compartilhar-grupo":
                case "compart-grupo":
                    if (!args.Player.Group.HasPermission(HouseRegionsPlugin.ShareWithGroups_Permission))
                    {
                        args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                        return true;
                    }

                    this.HouseShareGroupCommand_Exec(args);
                    return true;
                case "unsharegroup":
                case "descompartilhar-grupo":
                case "descompart-grupo":
                case "unshareg":
                    if (!args.Player.Group.HasPermission(HouseRegionsPlugin.ShareWithGroups_Permission))
                    {
                        args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                        return true;
                    }

                    this.HouseUnshareGroupCommand_Exec(args);
                    return true;
                case "reloadconfiguration":
                case "reloadconfig":
                case "reload":
                case "reloadcfg":
                    {
                        if (!args.Player.Group.HasPermission(HouseRegionsPlugin.Cfg_Permission))
                        {
                            args.Player.SendErrorMessage("[Loteamento] Você não tem permissão para executar essa ação!");
                            return true;
                        }

                        if (args.Parameters.Count == 2 && args.Parameters[1].Equals("ajuda", StringComparison.InvariantCultureIgnoreCase) || args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                        {
                            args.Player.SendMessage("Referência ao comando /lote relaod (Página 1/1)", Color.Lime);
                            args.Player.SendMessage("/lote reload", Color.White);
                            args.Player.SendMessage("Recarrega as configurações do Plugin de Loteamento e aplica as novas configurações.", Color.LightGray);
                            return true;
                        }

                        this.PluginTrace.WriteLineInfo("[Loteamento] Configurações recarregadas.");
                        try
                        {
                            this.Config = this.ReloadConfigurationCallback();
                            this.PluginTrace.WriteLineInfo("[Loteamento] Arquivo de configuração recarregado.");

                            if (args.Player != TSPlayer.Server)
                                args.Player.SendSuccessMessage("[Loteamento] Arquivo de configuração recarregado com sucesso.");
                        }
                        catch (Exception ex)
                        {
                            this.PluginTrace.WriteLineError(
                              "[Loteamento] Recarregar o arquivo de configuração falhou. Mantendo configuração atual. Detalhes do erro:\n{0}", ex
                            );
                        }

                        return true;
                    }
            }

            return false;
        }

        private bool RootCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return true;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, null, out pageNumber))
                return false;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Visão Geral do Loteamento (Página 1/2)", Color.Lime);
                    args.Player.SendMessage("Esse plugin oferece aos jogadores do servidor a possibilidade de definir uma proteção para suas construções.", Color.LightGray);
                    args.Player.SendMessage("Para mais informações sobre definir novos lotes use o comando [c/FFD700:/lote demarcar ajuda]", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("Caso queira informações de como compartilhar seu lote com outros jogadores, continue na página seguinte.", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("Para isso, você pode selecionar usuários específicos e permitir o acesso de modificação ao seu lote. ", Color.LightGray);
                    args.Player.SendMessage("Veja mais informações de como compartilhar seu lote executando  [c/ffd700:/lote compart ajuda]", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.LightGray);
                    args.Player.SendMessage("Deseja verificar informações de um lote? Execute [c/ffd700:/lote info]", Color.LightGray);
                    break;
            }

            return true;
        }
        #endregion

        #region [Command Handling /lote summary]
        private void HouseSummaryCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber = 1;
            if (args.Parameters.Count > 2)
            {
                if (args.Parameters[1].Equals("ajuda", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.HouseSummaryCommand_HelpCallback(args);
                    return;
                }

                args.Player.SendErrorMessage("Uso Correto: /lote resumo [pagina]");
                args.Player.SendInfoMessage("Digite [c/ffd700:/lote resumo ajuda] para obter informações sobre o uso desse comando.");
                return;
            }

            var ownerHouses = new Dictionary<string, int>(TShock.Regions.Regions.Count);
            for (int i = 0; i < TShock.Regions.Regions.Count; i++)
            {
                Region tsRegion = TShock.Regions.Regions[i];
                string owner;
                int dummy;
                if (!this.HousingManager.TryGetHouseRegionData(tsRegion.Name, out owner, out dummy))
                    continue;

                int houseCount;
                if (!ownerHouses.TryGetValue(owner, out houseCount))
                    ownerHouses.Add(owner, 1);
                else
                    ownerHouses[owner] = houseCount + 1;
            }

            IEnumerable<string> ownerHousesTermSelector = ownerHouses.Select(
              pair => string.Concat(pair.Key, " (", pair.Value, ")")
            );

            PaginationTools.SendPage(
              args.Player, pageNumber, PaginationTools.BuildLinesFromTerms(ownerHousesTermSelector), new PaginationTools.Settings
              {
                  HeaderFormat = string.Format("Donos do Lote ({{0}}/{{1}}):"),
                  FooterFormat = string.Format("Digite [c/ffd700:/lote resumo {{0}}] para ver mais."),
                  NothingToDisplayString = "Não há nenhum lote definido nesse mapa."
              }
            );
        }

        private void HouseSummaryCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao comando [c/ffd700:/lote resumo] (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("[c/ffd700:/lote resumo <pagina>]", Color.White);
                    args.Player.SendMessage("Mostra todos os Donos do lote e a quantidade de lotes que eles Administram.", Color.LightGray);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote info]
        private void HouseInfoCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber = 1;
            if (args.Parameters.Count > 2)
            {
                if (args.Parameters[1].Equals("ajuda", StringComparison.InvariantCultureIgnoreCase) || args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.HouseInfoCommand_HelpCallback(args);
                    return;
                }

                args.Player.SendErrorMessage("Uso Correto: [c/ffd700:/lote info <pagina>]");
                args.Player.SendInfoMessage("Digite [c/ffd700:/lote info ajuda] para obter informações sobre o uso desse comando.");
                return;
            }

            string owner;
            Region region;
            if (!this.TryGetHouseRegionAtPlayer(args.Player, out owner, out region))
                return;

            List<string> lines = new List<string> {
        "Dono: " + owner
      };

            if (region.AllowedIDs.Count > 0)
            {
                IEnumerable<string> sharedUsersSelector = region.AllowedIDs.Select(userId =>
                {
                    User user = TShock.Users.GetUserByID(userId);
                    if (user != null)
                        return user.Name;
                    else
                        return string.Concat("{ID: ", userId, "}");
                });

                List<string> extraLines = PaginationTools.BuildLinesFromTerms(sharedUsersSelector.Distinct());
                extraLines[0] = "Compartilhado com: " + extraLines[0];
                lines.AddRange(extraLines);
            }
            else
            {
                lines.Add("Não é compartilhado com nenhum usuário.");
            }

            if (region.AllowedGroups.Count > 0)
            {
                List<string> extraLines = PaginationTools.BuildLinesFromTerms(region.AllowedGroups.Distinct());
                extraLines[0] = "Shared with groups: " + extraLines[0];
                lines.AddRange(extraLines);
            }
            else
            {
                lines.Add("Não é compartilhado com nenhum grupo de usuários.");
            }

            PaginationTools.SendPage(
              args.Player, pageNumber, lines, new PaginationTools.Settings
              {
                  HeaderFormat = string.Format("Informações sobre o Lote ({{0}}/{{1}}):"),
                  FooterFormat = string.Format("Digite [c/ffd700:/lote info {{0}}] para mais informações.")
              }
            );

            this.SendAreaDottedFakeWiresTimed(args.Player, region.Area, 5000);
        }

        private void HouseInfoCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando [c/ffd700:/lote info] (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("/lote info [page]", Color.White);
                    args.Player.SendMessage("Mostra diversas informações sobre o Lote em sua posição.", Color.LightGray);
                    args.Player.SendMessage("Também irá mostrar a área da casa destacado por [c/ffd700:fios] que desaparecerão após alguns segundos.", Color.LightGray);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote scan]
        private void HouseScanCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count > 1)
            {
                if (args.Parameters[1].Equals("ajuda", StringComparison.InvariantCultureIgnoreCase) || args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.HouseScanCommand_HelpCallback(args);
                    return;
                }

                args.Player.SendErrorMessage("Uso Correto: /lote scan");
                args.Player.SendInfoMessage("Digite [c/ffd700:/lote scan ajuda] para obter informações sobre o uso desse comando.");
                return;
            }

            Point playerLocation = new Point(args.Player.TileX, args.Player.TileY);
            List<Rectangle> houseAreasToDisplay = new List<Rectangle>(
              from r in TShock.Regions.Regions
              where Math.Sqrt(Math.Pow(playerLocation.X - r.Area.Center.X, 2) + Math.Pow(playerLocation.Y - r.Area.Center.Y, 2)) <= 200
              select r.Area
            );
            if (houseAreasToDisplay.Count == 0)
            {
                args.Player.SendSuccessMessage("Não há nenhum Lote por perto.");
                return;
            }

            foreach (Rectangle regionArea in houseAreasToDisplay)
                this.SendAreaDottedFakeWires(args.Player, regionArea);
            args.Player.SendInfoMessage("Segure um fio ou uma ferramenta eletrônica para ver todos os Lotes próximos.");

            System.Threading.Timer hideTimer = null;
            hideTimer = new System.Threading.Timer(state =>
            {
                foreach (Rectangle regionArea in houseAreasToDisplay)
                    this.SendAreaDottedFakeWires(args.Player, regionArea, false);

                // ReSharper disable AccessToModifiedClosure
                Debug.Assert(hideTimer != null);
                hideTimer.Dispose();
                // ReSharper restore AccessToModifiedClosure
            },
              null, 10000, Timeout.Infinite
            );
        }

        private void HouseScanCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando /lote scan (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("/lote scan", Color.White);
                    args.Player.SendMessage("Mostra todos os Lotes próximos a posição do seu personagem demarcados por [c/009fff:fios]", Color.LightGray);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote demarcar]
        private void HouseDefineCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count > 1)
            {
                if (args.Parameters[1].Equals("ajuda", StringComparison.InvariantCultureIgnoreCase) || args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.HouseDefineCommand_HelpCallback(args);
                    return;
                }

                args.Player.SendErrorMessage("Uso Correto: /lote demarcar");
                args.Player.SendInfoMessage("Digite [c/ffd700:/lote demarcar ajuda] para obter informações sobre o uso desse comando.");
                return;
            }

            if (!args.Player.IsLoggedIn)
            {
                args.Player.SendErrorMessage("Você precisa efetuar login para poder definir novos Lotes.");
                return;
            }

            DPoint point1 = DPoint.Empty;
            DPoint point2 = DPoint.Empty;
            Rectangle houseArea = Rectangle.Empty;
            args.Player.SendMessage("[Loteamento] Primeira Marcação", Color.IndianRed);
            args.Player.SendMessage("Marque o ponto esquerdo mais alto de seu lote [c/ffd700:modificando um bloco]", Color.MediumSpringGreen);
            args.Player.SendMessage("ou [c/009fff:colocando um fio usando uma chave inglesa].", Color.MediumSpringGreen);

            CommandInteraction interaction = this.StartOrResetCommandInteraction(args.Player, 60000);
            interaction.TileEditCallback += (playerLocal, editType, tileId, tileLocation, objectStyle) =>
            {
                // Revoke Mark 1 or 2
                if (editType == TileEditType.DestroyWire || editType == TileEditType.DestroyWireBlue || editType == TileEditType.DestroyWireGreen || editType == TileEditType.DestroyWireYellow)
                {
                    if (tileLocation == point1)
                    {
                        point1 = DPoint.Empty;

                        if (houseArea != Rectangle.Empty)
                            this.SendAreaDottedFakeWires(playerLocal, houseArea, false);

                        playerLocal.SendTileSquare(tileLocation);

                        if (point2 != DPoint.Empty)
                            this.SendFakeWireCross(playerLocal, point2);

                        args.Player.SendMessage("[Loteamento] Primeira Marcação", Color.IndianRed);
                        args.Player.SendMessage("Marque o ponto esquerdo mais alto de seu lote [c/ffd700:modificando um bloco]", Color.MediumSpringGreen);
                        args.Player.SendMessage("ou [c/009fff:colocando um fio usando uma chave inglesa].", Color.MediumSpringGreen);
                        interaction.ResetTimer();
                    }
                    else if (tileLocation == point2)
                    {
                        point2 = DPoint.Empty;

                        if (houseArea != Rectangle.Empty)
                            this.SendAreaDottedFakeWires(playerLocal, houseArea, false);

                        playerLocal.SendTileSquare(tileLocation);

                        if (point1 != DPoint.Empty)
                            this.SendFakeWireCross(playerLocal, point1);

                        args.Player.SendMessage("[Loteamento] Segunda Marcação", Color.IndianRed);
                        args.Player.SendMessage("Marque o ponto direto mais baixo de seu lote [c/ffd700:modificando um bloco]", Color.MediumSpringGreen);
                        args.Player.SendMessage("ou [c/009fff:colocando um fio usando uma chave inglesa].", Color.MediumSpringGreen);
                        interaction.ResetTimer();
                    }
                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = false };
                }

                // Mark 1 / 2
                if (point1 == DPoint.Empty || point2 == DPoint.Empty)
                {
                    if (point1 == DPoint.Empty)
                        point1 = tileLocation;
                    else
                        point2 = tileLocation;

                    playerLocal.SendTileSquare(tileLocation);
                    this.SendFakeWireCross(playerLocal, tileLocation);

                    if (point1 != DPoint.Empty && point2 != DPoint.Empty)
                    {
                        houseArea = new Rectangle(
                          Math.Min(point1.X, point2.X), Math.Min(point1.Y, point2.Y),
                          Math.Abs(point1.X - point2.X), Math.Abs(point1.Y - point2.Y)
                        );
                        this.SendAreaDottedFakeWires(playerLocal, houseArea);

                        args.Player.SendMessage("[Loteamento] Marcação de Confirmação", Color.IndianRed);
                        args.Player.SendMessage("Marque qualquer ponto dentro da área selecionada anteriormente para confirmar a criação do lote, ou qualquer ponto fora da área selecionada para cancelar.", Color.MediumSpringGreen);
                    }
                    else
                    {
                        if (point2 == DPoint.Empty)
                        {
                            args.Player.SendMessage("[Loteamento] Segunda Marcação", Color.IndianRed);
                            args.Player.SendMessage("Marque o ponto direto mais baixo de seu lote [c/ffd700:modificando um bloco]", Color.MediumSpringGreen);
                            args.Player.SendMessage("ou [c/009fff:colocando um fio usando uma chave inglesa].", Color.MediumSpringGreen);
                        }
                        else
                        {
                            args.Player.SendMessage("[Loteamento] Primeira Marcação", Color.IndianRed);
                            args.Player.SendMessage("Marque o ponto esquerdo mais alto de seu lote [c/ffd700:modificando um bloco]", Color.MediumSpringGreen);
                            args.Player.SendMessage("ou [c/009fff:colocando um fio usando uma chave inglesa].", Color.MediumSpringGreen);
                        }
                    }

                    interaction.ResetTimer();

                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = false };
                }
                else
                {
                    // Final Mark
                    playerLocal.SendTileSquare(point1);
                    playerLocal.SendTileSquare(point2);
                    this.SendAreaDottedFakeWires(playerLocal, houseArea, false);
                    playerLocal.SendTileSquare(tileLocation);

                    if (
                      tileLocation.X >= houseArea.Left && tileLocation.X <= houseArea.Right &&
                      tileLocation.Y >= houseArea.Top && tileLocation.Y <= houseArea.Bottom
                    )
                    {
                        try
                        {
                            if (houseArea.Width <= 0 || houseArea.Height <= 0)
                            {
                                playerLocal.SendErrorMessage("[Loteamento] A área selecionada para o lote é inválida.");
                            }
                            else
                            {
                                this.HousingManager.CreateHouseRegion(playerLocal, houseArea, true, true);
                                playerLocal.SendMessage("[Loteamento] Lote definido e protegido com sucesso.", Color.MediumSpringGreen);
                            }
                        }
                        catch (InvalidHouseSizeException ex)
                        {
                            this.ExplainInvalidRegionSize(playerLocal, houseArea, ex.RestrictingConfig);
                        }
                        catch (HouseOverlapException)
                        {
                            if (this.Config.AllowTShockRegionOverlapping)
                            {
                                playerLocal.SendErrorMessage("[Loteamento] A área selecionada está sobrepondo outro lote.");
                            }
                            else
                            {
                                playerLocal.SendErrorMessage("[Loteamento] A área selecionada está sobrepondo outro lote.");
                            }
                        }
                        catch (LimitEnforcementException)
                        {
                            playerLocal.SendErrorMessage(
                              "[Loteamento] Você atingiu o limite máximo de {0} lotes.\nDelete pelo menos um lote antes de poder criar um novo.",
                              this.Config.MaxHousesPerUser
                            );
                        }
                    }
                    else
                    {
                        playerLocal.SendWarningMessage("[Loteamento] A criação do lote foi cancelada.");
                    }

                    return new CommandInteractionResult { IsHandled = true, IsInteractionCompleted = true };
                }
            };
            interaction.TimeExpiredCallback += (playerLocal) =>
            {
                playerLocal.SendErrorMessage("[Loteamento] Tempo de espera excedido. Nenhum lote foi definido.");
            };
            interaction.AbortedCallback += (playerLocal) =>
            {
                if (point1 != DPoint.Empty)
                    playerLocal.SendTileSquare(point1);
                if (point2 != DPoint.Empty)
                    playerLocal.SendTileSquare(point2);
                if (houseArea != Rectangle.Empty)
                    this.SendAreaDottedFakeWires(playerLocal, houseArea, false);
            };
        }

        private void HouseDefineCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando [c/ffd700:/lote demarcar] (Página 1/2)", Color.Lime);
                    args.Player.SendMessage("/lote demarcar|def", Color.White);
                    args.Player.SendMessage("Modo de criação de novo lote.", Color.LightGray);
                    args.Player.SendMessage("Digite [c/ffd700:/lote demarcar] e siga as instruções para proteger a área selecionada.", Color.LightGray);
                    args.Player.SendMessage("NOTA: Utilizar uma chave inglesa facilita a demarcação da área de seu lote, mas também é possível utilizar uma picareta ou qualquer coisa que modifique um bloco ou objeto", Color.IndianRed);
                    return;
                case 2:
                    args.Player.SendMessage("Lotes já existentes podem ser redimensionados usando o comando [c/ffd700:/lote redim].", Color.LightGray);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote redim]
        private void HouseResizeCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            Action invalidSyntax = () =>
            {
                args.Player.SendErrorMessage("Uso Correto: /lote redim <cima/c|baixo/b|esquerda/e|direita/d>[...] <quadrados>");
                args.Player.SendInfoMessage("Digite /lote redim ajuda para obter informações sobre o uso desse comando.");
            };

            if (args.Parameters.Count >= 2 && args.Parameters[1].Equals("ajuda", StringComparison.InvariantCultureIgnoreCase) || args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
            {
                this.HouseResizeCommand_HelpCallback(args);
                return;
            }

            Region region;
            string owner;
            if (!this.TryGetAccessibleHouseRegionAtPlayer(args.Player, out owner, out region))
                return;

            int amount;
            if (args.Parameters.Count < 3 || !int.TryParse(args.Parameters[args.Parameters.Count - 1], out amount))
            {
                invalidSyntax();
                return;
            }

            Rectangle newArea = region.Area;
            List<int> directions = new List<int>();
            //0 = up
            //1 = right
            //2 = down
            //3 = left
            for (int i = 1; i < args.Parameters.Count - 1; i++)
            {
                switch (args.Parameters[i].ToLowerInvariant())
                {
                    case "up":
                    case "u":
                    case "cima":
                    case "c":
                        newArea.Y -= amount;
                        newArea.Height += amount;
                        directions.Add(0);
                        break;
                    case "down":
                    case "baixo":
                    case "b":
                        newArea.Height += amount;
                        directions.Add(2);
                        break;
                    case "left":
                    case "l":
                    case "esquerda":
                    case "e":
                        newArea.X -= amount;
                        newArea.Width += amount;
                        directions.Add(3);
                        break;
                    case "right":
                    case "r":
                    case "direita":
                    case "d":
                        newArea.Width += amount;
                        directions.Add(1);
                        break;
                }
            }

            if (newArea.Width < 0)
                newArea.Width = 1;
            if (newArea.Height < 0)
                newArea.Height = 1;

            Configuration.HouseSizeConfig restrictingSizeConfig;
            if (!this.HousingManager.CheckHouseRegionValidSize(newArea, out restrictingSizeConfig))
            {
                this.ExplainInvalidRegionSize(args.Player, newArea, restrictingSizeConfig);
                return;
            }

            if (this.HousingManager.CheckHouseRegionOverlap(owner, newArea))
            {
                if (this.Config.AllowTShockRegionOverlapping)
                {
                    args.Player.SendErrorMessage("[Loteamento] A área selecionada para redimensionar sobrepõe outro lote");
                }
                else
                {
                    args.Player.SendErrorMessage("[Loteamento] A área selecionada para redimensionar sobrepõe outro lote");
                }

                return;
            }

            Rectangle oldArea = region.Area;
            region.Area = newArea;
            foreach (int direction in directions)
            {
                if (!TShock.Regions.ResizeRegion(region.Name, amount, direction))
                {
                    args.Player.SendErrorMessage("[Loteamento] Ocorreu um erro interno.");
                    region.Area = oldArea;
                    return;
                }
            }

            args.Player.SendSuccessMessage("[Loteamento] Lote redimensionado com sucesso.");
            this.SendAreaDottedFakeWires(args.Player, oldArea, false);
            this.SendAreaDottedFakeWiresTimed(args.Player, newArea, 5000);
        }

        private void HouseResizeCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando /lote redim (Página 1 of 3)", Color.Lime);
                    args.Player.SendMessage("/lote redim <cima|baixo|esquerda|direita> <quadrados>", Color.White);
                    args.Player.SendMessage("Redimensiona o Lote atual em uma direção com base no valor de quadrados informado", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.IndianRed);
                    args.Player.SendMessage("c|b|e|d = As direções que você pode redimensionar (cima, baixo, esquerda, direita).", Color.LightGray);
                    break;
                case 2:
                    args.Player.SendMessage("quadrados = A quantidade de blocos para expandir, também pode ser um valor negativo para diminuir a região do lote", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.IndianRed);
                    args.Player.SendMessage("NOTA: Se você segurar um fio ou alguma ferramenta eletrônica, você vai poder ver a nova área do lote após redimensionar.", Color.IndianRed);
                    break;
                case 3:
                    args.Player.SendMessage("NOTA: Você deve ter permissão de Dono ao lote para redimensiona-lo.", Color.IndianRed);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote deletar]
        private void HouseDeleteCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count > 1)
            {
                if (args.Parameters[1].Equals("ajuda", StringComparison.InvariantCultureIgnoreCase) || args.Parameters[1].Equals("help", StringComparison.InvariantCultureIgnoreCase))
                {
                    this.HouseDeleteCommand_HelpCallback(args);
                    return;
                }

                args.Player.SendErrorMessage("Uso Correto: /lote deletar");
                args.Player.SendInfoMessage("Digite [c/ffd700:/lote deletar ajuda] para obter informações sobre o uso desse comando.");
                return;
            }

            Region region;
            if (!this.TryGetAccessibleHouseRegionAtPlayer(args.Player, out region))
                return;

            if (!TShock.Regions.DeleteRegion(region.Name))
            {
                args.Player.SendErrorMessage("[Loteamento] Ocorreu um erro interno.");
                return;
            }

            args.Player.SendSuccessMessage("[Loteamento] Lote deletado com sucesso.");
        }

        private void HouseDeleteCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando /lote deletar (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("/lote deletar|del", Color.White);
                    args.Player.SendMessage("Deleta o Lote que está em sua posição atual.", Color.LightGray);
                    args.Player.SendMessage("NOTA: Você deve ter permissão de Dono ao lote para deleta-lo.", Color.IndianRed);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote alterar-dono]
        private void HouseSetOwnerCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Uso Correto: /lote alterar-dono <usuário>");
                args.Player.SendInfoMessage("Digite [c/ffd700:/lote alterar-dono ajuda] para obter informações sobre o uso desse comando.");
                return;
            }

            string newOwnerRaw = args.ParamsToSingleString(1);
            if (newOwnerRaw.Equals("help", StringComparison.InvariantCultureIgnoreCase) || newOwnerRaw.Equals("ajuda", StringComparison.InvariantCultureIgnoreCase))
            {
                this.HouseSetOwnerCommand_HelpCallback(args);
                return;
            }

            User tsUser;
            if (!TShockEx.MatchUserByPlayerName(newOwnerRaw, out tsUser, args.Player))
                return;

            Region region;
            if (!this.TryGetAccessibleHouseRegionAtPlayer(args.Player, out region))
                return;

            if (tsUser.Name == region.Owner)
            {
                args.Player.SendErrorMessage($"[Loteamento] {tsUser.Name} já é o dono desse lote.");
                return;
            }

            Group tsGroup = TShock.Groups.GetGroupByName(tsUser.Group);
            if (tsGroup == null)
            {
                args.Player.SendErrorMessage("[Loteamento] Ocorreu um erro ao determinar o grupo TShock do novo dono.");
                return;
            }

            try
            {
                this.HousingManager.CreateHouseRegion(tsUser, tsGroup, region.Area, false, true, false);
            }
            catch (LimitEnforcementException)
            {
                args.Player.SendErrorMessage("[Loteamento] O novo dono do lote atingiu seu limite máximo de lotes.");
                return;
            }
            catch (Exception ex)
            {
                args.Player.SendErrorMessage("[Loteamento] Ocorreu um erro interno: " + ex.Message);
                return;
            }

            if (!TShock.Regions.DeleteRegion(region.Name))
            {
                args.Player.SendErrorMessage("[Loteamento] Ocorreu um erro interno ao recriar o lote.");
                return;
            }

            args.Player.SendSuccessMessage($"[Loteamento] O dono desse lote agora é \"{tsUser.Name}\" e todos os usuários com permissão de modificação nesse lote foram removidos.");
        }

        private void HouseSetOwnerCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando /lote alterar-dono (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("/lote alterar-dono <usuário>", Color.White);
                    args.Player.SendMessage("Altera o dono do lote em que seu personagem está atualmente.", Color.LightGray);
                    args.Player.SendMessage("NOTA: Você deve ter permissão de Dono ao lote para realizar esta ação.", Color.IndianRed);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote compartilhar]
        private void HouseShareCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Uso Correto: /lote compartilhar <usuário>");
                args.Player.SendInfoMessage("Digite [c/ffd700:/lote compartilhar ajuda] para obter informações sobre o uso desse comando.");
                return;
            }

            string shareTargetRaw = args.ParamsToSingleString(1);
            if (shareTargetRaw.Equals("help", StringComparison.InvariantCultureIgnoreCase) || shareTargetRaw.Equals("ajuda", StringComparison.InvariantCultureIgnoreCase))
            {
                this.HouseShareCommand_HelpCallback(args);
                return;
            }

            User tsUser;
            if (!TShockEx.MatchUserByPlayerName(shareTargetRaw, out tsUser, args.Player))
                return;

            Region region;
            if (!this.TryGetAccessibleHouseRegionAtPlayer(args.Player, out region))
                return;

            if (!TShock.Regions.AddNewUser(region.Name, tsUser.Name))
            {
                args.Player.SendErrorMessage("[Loteamento] Ocorreu um erro interno.");
                return;
            }

            args.Player.SendSuccessMessage("[Loteamento] Agora o usuário \"{0}\" tem permissão para construir dentro desse lote.", tsUser.Name);
        }

        private void HouseShareCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando /lote compartilhar (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("/lote compartilhar|compart <usuário>", Color.White);
                    args.Player.SendMessage("Autoriza outro jogador a construir dentro do lote em que seu personagem está atualmente.", Color.LightGray);
                    args.Player.SendMessage("NOTA: Você deve ter permissão de Dono ao lote para realizar essa ação.", Color.IndianRed);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote descompartilhar]
        private void HouseUnshareCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Uso Correto: /lote descompartilhar <usuário>");
                args.Player.SendInfoMessage("Digite [c/ffd700:/lote descompartilhar] ajuda para obter informações sobre o uso desse comando.");
                return;
            }

            string shareTargetRaw = args.ParamsToSingleString(1);
            if (shareTargetRaw.Equals("help", StringComparison.InvariantCultureIgnoreCase) || shareTargetRaw.Equals("ajuda", StringComparison.InvariantCultureIgnoreCase))
            {
                this.HouseUnshareCommand_HelpCallback(args);
                return;
            }

            User tsUser;
            if (!TShockEx.MatchUserByPlayerName(shareTargetRaw, out tsUser, args.Player))
                return;

            Region region;
            if (!this.TryGetAccessibleHouseRegionAtPlayer(args.Player, out region))
                return;

            if (!TShock.Regions.RemoveUser(region.Name, tsUser.Name))
            {
                args.Player.SendErrorMessage("[Loteamento] Ocorreu um erro interno.");
                return;
            }

            args.Player.SendSuccessMessage("[Loteamento] Permissão de construção do usuário \"{0}\" removida.", tsUser.Name);
        }

        private void HouseUnshareCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando /lote descompartilhar (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("/lote descompartilhar|descompart <usuário>", Color.White);
                    args.Player.SendMessage("Desautoriza outro jogador a construir dentro do lote em que seu personagem está atualmente.", Color.LightGray);
                    args.Player.SendMessage("NOTA: Você deve ter permissão de Dono ao lote para realizar essa ação.", Color.IndianRed);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote compartilhargroup]
        private void HouseShareGroupCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Uso Correto: /lote compartilhargroup <group name>");
                args.Player.SendInfoMessage("Digite /lote compartilhargroup ajuda para obter informações sobre o uso desse comando.");
                return;
            }

            string shareTargetRaw = args.ParamsToSingleString(1);
            if (shareTargetRaw.Equals("help", StringComparison.InvariantCultureIgnoreCase))
            {
                this.HouseShareGroupCommand_HelpCallback(args);
                return;
            }

            Group tsGroup = TShock.Groups.GetGroupByName(shareTargetRaw);
            if (tsGroup == null)
            {
                args.Player.SendErrorMessage("A group with the name \"{0}\" does not exist.", shareTargetRaw);
                return;
            }

            Region region;
            if (!this.TryGetAccessibleHouseRegionAtPlayer(args.Player, out region))
                return;

            if (!TShock.Regions.AllowGroup(region.Name, tsGroup.Name))
            {
                args.Player.SendErrorMessage("Ocorreu um erro interno.");
                return;
            }

            args.Player.SendSuccessMessage("All users of group \"{0}\" have build access to this house now.", tsGroup.Name);
        }

        private void HouseShareGroupCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando /lote compartilhargroup (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("/lote compartilhargroup|shareg <group name>", Color.White);
                    args.Player.SendMessage("Grants build access to all users in a TShock group for the house at you character.", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.IndianRed);
                    args.Player.SendMessage("NOTA: Você deve ter permissão de Dono ao lote in order to share it, just having", Color.IndianRed);
                    args.Player.SendMessage("build access is not sufficient.", Color.IndianRed);
                    return;
            }
        }
        #endregion

        #region [Command Handling /lote descompartilhargroup]
        private void HouseUnshareGroupCommand_Exec(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            if (args.Parameters.Count < 2)
            {
                args.Player.SendErrorMessage("Uso Correto: /lote descompartilhargroup <group name>");
                args.Player.SendInfoMessage("Digite /lote descompartilhargroup ajuda para obter informações sobre o uso desse comando.");
                return;
            }

            string shareTargetRaw = args.ParamsToSingleString(1);
            if (shareTargetRaw.Equals("help", StringComparison.InvariantCultureIgnoreCase))
            {
                this.HouseUnshareGroupCommand_HelpCallback(args);
                return;
            }

            Group tsGroup = TShock.Groups.GetGroupByName(shareTargetRaw);
            if (tsGroup == null)
            {
                args.Player.SendErrorMessage("A group with the name \"{0}\" does not exist.", shareTargetRaw);
                return;
            }

            Region region;
            if (!this.TryGetAccessibleHouseRegionAtPlayer(args.Player, out region))
                return;

            if (!TShock.Regions.RemoveGroup(region.Name, tsGroup.Name))
            {
                args.Player.SendErrorMessage("Ocorreu um erro interno.");
                return;
            }

            args.Player.SendSuccessMessage("Users of group \"{0}\" have no more build access to this house anymore.", tsGroup.Name);
        }

        private void HouseUnshareGroupCommand_HelpCallback(CommandArgs args)
        {
            if (args == null || this.IsDisposed)
                return;

            int pageNumber;
            if (!PaginationTools.TryParsePageNumber(args.Parameters, 2, args.Player, out pageNumber))
                return;

            switch (pageNumber)
            {
                default:
                    args.Player.SendMessage("Referência ao Comando /lote descompartilhargroup (Página 1/1)", Color.Lime);
                    args.Player.SendMessage("/lote descompartilhargroup|unshareg <group name>", Color.White);
                    args.Player.SendMessage("Removes build access of all users in a TShock group for the house at you character.", Color.LightGray);
                    args.Player.SendMessage(string.Empty, Color.IndianRed);
                    args.Player.SendMessage("NOTA: Você deve ter permissão de Dono ao lote in order to alter shares of it,", Color.IndianRed);
                    args.Player.SendMessage("just having build access is not sufficient.", Color.IndianRed);
                    return;
            }
        }
        #endregion

        public bool TryGetHouseRegionAtPlayer(TSPlayer player, out string owner, out Region region)
        {
            if (player == null) throw new ArgumentNullException();

            int dummy;
            if (!this.HousingManager.TryGetHouseRegionAtPlayer(player, out owner, out dummy, out region))
            {
                player.SendErrorMessage("[Loteamento] Não há nenhum lote em sua posição atual.");
                return false;
            }

            return true;
        }

        public bool TryGetAccessibleHouseRegionAtPlayer(TSPlayer player, out string owner, out Region region)
        {
            if (player == null) throw new ArgumentNullException();

            if (!this.TryGetHouseRegionAtPlayer(player, out owner, out region))
                return false;

            if (player.User.Name != owner && !player.Group.HasPermission(HouseRegionsPlugin.HousingMaster_Permission))
            {
                player.SendErrorMessage("[Loteamento] Você não é o dono desse lote.");
                return false;
            }

            return true;
        }

        private bool TryGetAccessibleHouseRegionAtPlayer(TSPlayer player, out Region region)
        {
            string dummy;
            return this.TryGetAccessibleHouseRegionAtPlayer(player, out dummy, out region);
        }

        private void SendFakeTileWire(TSPlayer player, DPoint tileLocation)
        {
            ITile tile = TerrariaUtils.Tiles[tileLocation];
            if (tile.wire2())
                return;

            try
            {
                tile.wire2(true);
                player.SendTileSquare(tileLocation, 1);
            }
            finally
            {
                tile.wire2(false);
            }
        }

        private void SendAreaDottedFakeWiresTimed(TSPlayer player, Rectangle area, int timeMs)
        {
            this.SendAreaDottedFakeWires(player, area);

            System.Threading.Timer hideTimer = null;
            hideTimer = new System.Threading.Timer(state =>
            {
                this.SendAreaDottedFakeWires(player, area, false);

                // ReSharper disable AccessToModifiedClosure
                Debug.Assert(hideTimer != null);
                hideTimer.Dispose();
                // ReSharper restore AccessToModifiedClosure
            },
              null, timeMs, Timeout.Infinite
            );
        }

        private void SendAreaDottedFakeWires(TSPlayer player, Rectangle area, bool setOrUnset = true)
        {
            foreach (Point boundaryPoint in TShock.Utils.EnumerateRegionBoundaries(area))
                if ((boundaryPoint.X + boundaryPoint.Y & 1) == 0)
                    if (setOrUnset)
                        this.SendFakeTileWire(player, new DPoint(boundaryPoint.X, boundaryPoint.Y));
                    else
                        player.SendTileSquare(boundaryPoint.X, boundaryPoint.Y, 1);
        }

        private void SendFakeWireCross(TSPlayer player, DPoint crossLocation)
        {
            this.SendFakeTileWire(player, crossLocation);
            this.SendFakeTileWire(player, crossLocation.OffsetEx(-1, 0));
            this.SendFakeTileWire(player, crossLocation.OffsetEx(1, 0));
            this.SendFakeTileWire(player, crossLocation.OffsetEx(0, -1));
            this.SendFakeTileWire(player, crossLocation.OffsetEx(0, 1));
        }

        private void ExplainInvalidRegionSize(TSPlayer toPlayer, Rectangle area, Configuration.HouseSizeConfig restrictingConfig)
        {
            if (restrictingConfig.Equals(this.Config.MinSize))
            {
                toPlayer.SendErrorMessage("A área selecionada é muito pequena:");
                toPlayer.SendErrorMessage("Largura Mínima: {0} (largura definida {1}).", restrictingConfig.Width, area.Width);
                toPlayer.SendErrorMessage("Altura Mínima: {0} (altura definida {1}).", restrictingConfig.Height, area.Height);
                toPlayer.SendErrorMessage("Blocos Mínimos: {0} (quantidade atual {1}).", restrictingConfig.TotalTiles, area.Width * area.Height);
            }
            else
            {
                toPlayer.SendErrorMessage("A área selecionada é muito grande:");
                toPlayer.SendErrorMessage("Largura Máxima: {0} (largura definida {1}).", restrictingConfig.Width, area.Width);
                toPlayer.SendErrorMessage("Altura Máxima: {0} (altura definida {1}).", restrictingConfig.Height, area.Height);
                toPlayer.SendErrorMessage("Blocos Máximos: {0} (quantidade atual {1}).", restrictingConfig.TotalTiles, area.Width * area.Height);
            }
        }

        #region [IDisposable Implementation]
        protected override void Dispose(bool isDisposing)
        {
            if (this.IsDisposed)
                return;

            if (isDisposing)
                this.ReloadConfigurationCallback = null;

            base.Dispose(isDisposing);
        }
        #endregion
    }
}
