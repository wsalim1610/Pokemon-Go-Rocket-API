using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using AllEnum;
using PokemonGo.RocketAPI.Enums;
using PokemonGo.RocketAPI.Exceptions;
using PokemonGo.RocketAPI.Extensions;
using PokemonGo.RocketAPI.GeneratedCode;
using System.Configuration;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using GMap.NET.WindowsForms.ToolTips;
using System.Threading;
using BrightIdeasSoftware;
using PokemonGo.RocketAPI.Helpers;

namespace PokemonGo.RocketAPI.Window
{
    public partial class MainForm : Form
    {
        public static MainForm Instance;
        public static SynchronizationContext synchronizationContext;

        private GMapOverlay searchAreaOverlay = new GMapOverlay("areas");
        private GMapOverlay pokestopsOverlay = new GMapOverlay("pokestops");
        private GMapOverlay pokemonsOverlay = new GMapOverlay("pokemons");
        private GMapOverlay playerOverlay = new GMapOverlay("players");

        private GMarkerGoogle playerMarker;

        private IEnumerable<FortData> pokeStops;
        private IEnumerable<MapPokemon> wildPokemons;

        public MainForm()
        {
            InitializeComponent();
            synchronizationContext = SynchronizationContext.Current;
            ClientSettings = Settings.Instance;
            Client.OnConsoleWrite += Client_OnConsoleWrite;
            Instance = this;
        }

        public static ISettings ClientSettings;
        private static int Currentlevel = -1;
        private static int TotalExperience = 0;
        private static int TotalPokemon = 0;
        private static bool Stopping = false;
        private static DateTime TimeStarted = DateTime.Now;
        public static DateTime InitSessionDateTime = DateTime.Now;
        private static bool IsBotStarted = false;
        
        private Client botClient;
        private Client pokemonListClient;
        private LocationManager locationManager;
        
        public void CheckVersion()
        {
            try
            {
                var match =
                    new Regex(
                        @"\[assembly\: AssemblyVersion\(""(\d{1,})\.(\d{1,})\.(\d{1,})\.(\d{1,})""\)\]")
                        .Match(DownloadServerVersion());

                if (!match.Success) return;
                var gitVersion =
                    new Version(
                        string.Format(
                            "{0}.{1}.{2}.{3}",
                            match.Groups[1],
                            match.Groups[2],
                            match.Groups[3],
                            match.Groups[4]));
                // makes sense to display your version and say what the current one is on github
                ColoredConsoleWrite(Color.Green, "Your version is " + Assembly.GetExecutingAssembly().GetName().Version);
                ColoredConsoleWrite(Color.Green, "Github version is " + gitVersion);
                ColoredConsoleWrite(Color.Green, "You can find it at www.GitHub.com/DetectiveSquirrel/Pokemon-Go-Rocket-API");
            }
            catch (Exception)
            {
                ColoredConsoleWrite(Color.Red, "Unable to check for updates now...");
            }
        }

        private static string DownloadServerVersion()
        {
            using (var wC = new WebClient())
                return
                    wC.DownloadString(
                        "https://raw.githubusercontent.com/DetectiveSquirrel/Pokemon-Go-Rocket-API/master/PokemonGo/RocketAPI/Window/Properties/AssemblyInfo.cs");
        }

        #region BOT
        private async void Execute()
        {
            if (botClient == null)
            {
                botClient = new Client(ClientSettings);
                this.locationManager = new LocationManager(botClient, ClientSettings.TravelSpeed);
            }
            try
            {
                if (!botClient.HasServerSet())
                {
                    switch (ClientSettings.AuthType)
                    {
                        case AuthType.Ptc:
                            ColoredConsoleWrite(Color.Green, "Login Type: Pokemon Trainers Club");
                            break;
                        case AuthType.Google:
                            ColoredConsoleWrite(Color.Green, "Login Type: Google");
                            break;
                    }
                    await botClient.Login();
                    await botClient.SetServer();
                }
                var profile = await botClient.GetProfile();
                var settings = await botClient.GetSettings();
                var mapObjects = await botClient.GetMapObjects();
                var inventory = await botClient.GetInventory();
                var pokemons =
                    inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                        .Where(p => p != null && p?.PokemonId > 0);

                ConsoleLevelTitle(profile.Profile.Username, botClient);

                // Write the players ingame details
                ColoredConsoleWrite(Color.Yellow, "----------------------------");
                /*// dont actually want to display info but keeping here incase people want to \O_O/
                 * if (ClientSettings.AuthType == AuthType.Ptc)
                {
                    ColoredConsoleWrite(Color.Cyan, "Account: " + ClientSettings.PtcUsername);
                    ColoredConsoleWrite(Color.Cyan, "Password: " + ClientSettings.PtcPassword + "\n");
                }
                else
                {
                    ColoredConsoleWrite(Color.Cyan, "Email: " + ClientSettings.Email);
                    ColoredConsoleWrite(Color.Cyan, "Password: " + ClientSettings.Password + "\n");
                }*/
                ColoredConsoleWrite(Color.DarkGray, "Name: " + profile.Profile.Username);
                ColoredConsoleWrite(Color.DarkGray, "Team: " + profile.Profile.Team);
                if (profile.Profile.Currency.ToArray()[0].Amount > 0) // If player has any pokecoins it will show how many they have.
                    ColoredConsoleWrite(Color.DarkGray, "Pokecoins: " + profile.Profile.Currency.ToArray()[0].Amount);
                ColoredConsoleWrite(Color.DarkGray, "Stardust: " + profile.Profile.Currency.ToArray()[1].Amount + "\n");
                ColoredConsoleWrite(Color.DarkGray, "Latitude: " + ClientSettings.DefaultLatitude);
                ColoredConsoleWrite(Color.DarkGray, "Longitude: " + ClientSettings.DefaultLongitude);
                try
                {
                    ColoredConsoleWrite(Color.DarkGray, "Country: " + CallAPI("country", ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude));
                    ColoredConsoleWrite(Color.DarkGray, "Area: " + CallAPI("place", ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude));
                }
                catch (Exception)
                {
                    ColoredConsoleWrite(Color.DarkGray, "Unable to get Country/Place");
                }

                ColoredConsoleWrite(Color.Yellow, "----------------------------");

                // I believe a switch is more efficient and easier to read.
                switch (ClientSettings.TransferType)
                {
                    case "Leave Strongest":
                        await TransferAllButStrongestUnwantedPokemon(botClient);
                        break;
                    case "All":
                        await TransferAllGivenPokemons(botClient, pokemons);
                        break;
                    case "Duplicate":
                        await TransferDuplicatePokemon(botClient);
                        break;
                    case "IV Duplicate":
                        await TransferDuplicateIVPokemon(botClient);
                        break;
                    case "CP":
                        await TransferAllWeakPokemon(botClient, ClientSettings.TransferCPThreshold);
                        break;
                    case "IV":
                        await TransferAllGivenPokemons(botClient, pokemons, ClientSettings.TransferIVThreshold);
                        break;
                    default:
                        ColoredConsoleWrite(Color.DarkGray, "Transfering pokemon disabled");
                        break;
                }


                if (ClientSettings.EvolveAllGivenPokemons)
                    await EvolveAllGivenPokemons(botClient, pokemons);
                if (ClientSettings.Recycler)
                    botClient.RecycleItems(botClient);

                await Task.Delay(5000);
                PrintLevel(botClient);
                await ExecuteFarmingPokestopsAndPokemons(botClient);

                if (!Stopping)
                {
                    ColoredConsoleWrite(Color.Red, $"No nearby useful locations found. Please wait 10 seconds.");
                    await Task.Delay(10000);
                    CheckVersion();
                    Execute();
                }
                else
                {
                    ConsoleClear();
                    ColoredConsoleWrite(Color.Red, $"Bot successfully stopped.");
                    synchronizationContext.Post(new SendOrPostCallback(o =>
                    {
                        startStopBotToolStripMenuItem.Enabled = true;
                        startStopBotToolStripMenuItem.Text = "Start Bot";
                    }), null);
                    Stopping = false;
                    IsBotStarted = false;
                    pokeStops = null;
                    botClient = null;
                }
            }
            catch (Exception ex)
            {
                ColoredConsoleWrite(Color.Red, ex.ToString());
                if (!Stopping)
                {
                    botClient = null;
                    Execute();
                }
            }
        }
        
        public void StartBot()
        {
            if (!IsBotStarted)
            {
                IsBotStarted = true;
                startStopBotToolStripMenuItem.Text = "Stop Bot";
                InitializeMap();
                Task.Run(() =>
                {
                    try
                    {
                        CheckVersion();
                        Execute();
                    }
                    catch (PtcOfflineException)
                    {
                        ColoredConsoleWrite(Color.Red, "PTC Servers are probably down OR your credentials are wrong. Try google");
                    }
                    catch (Exception ex)
                    {
                        ColoredConsoleWrite(Color.Red, $"Unhandled exception: {ex}");
                    }
                });
            }
        }

        public void StopBot()
        {
            if (IsBotStarted)
            {
                startStopBotToolStripMenuItem.Enabled = false;
                Stopping = true;
                ColoredConsoleWrite(Color.Red, $"Stopping the bot.. Waiting for the last action to be complete.");
            }
        }

        public async void RestartBot()
        {
            StopBot();
            while (botClient != null)
            {
                await Task.Delay(25);
            }
            StartBot();
        }
        #endregion

        #region AUTO EVOLVE POKEMON
        private async Task EvolvePokemons(Client client)
        {
            var inventory = await client.GetInventory();
            var pokemons =
                inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                    .Where(p => p != null && p?.PokemonId > 0);

            await EvolveAllGivenPokemons(client, pokemons);
        }

        private async Task EvolveAllGivenPokemons(Client client, IEnumerable<PokemonData> pokemonToEvolve)
        {
            foreach (var pokemon in pokemonToEvolve)
            {
                /*
                enum Holoholo.Rpc.Types.EvolvePokemonOutProto.Result {
	                UNSET = 0;
	                SUCCESS = 1;
	                FAILED_POKEMON_MISSING = 2;
	                FAILED_INSUFFICIENT_RESOURCES = 3;
	                FAILED_POKEMON_CANNOT_EVOLVE = 4;
	                FAILED_POKEMON_IS_DEPLOYED = 5;
                }
                }*/

                var countOfEvolvedUnits = 0;
                var xpCount = 0;

                EvolvePokemonOut evolvePokemonOutProto;
                do
                {
                    evolvePokemonOutProto = await client.EvolvePokemon(pokemon.Id);
                    //todo: someone check whether this still works

                    if (evolvePokemonOutProto.Result == 1)
                    {
                        ColoredConsoleWrite(Color.Cyan,
                            $"Evolved {pokemon.PokemonId} successfully for {evolvePokemonOutProto.ExpAwarded}xp");

                        countOfEvolvedUnits++;
                        xpCount += evolvePokemonOutProto.ExpAwarded;
                    }
                    else
                    {
                        var result = evolvePokemonOutProto.Result;
                        /*
                        ColoredConsoleWrite(ConsoleColor.White, $"Failed to evolve {pokemon.PokemonId}. " +
                                                 $"EvolvePokemonOutProto.Result was {result}");

                        ColoredConsoleWrite(ConsoleColor.White, $"Due to above error, stopping evolving {pokemon.PokemonId}");
                        */
                    }
                } while (evolvePokemonOutProto.Result == 1);
                if (countOfEvolvedUnits > 0)
                    ColoredConsoleWrite(Color.Cyan,
                        $"Evolved {countOfEvolvedUnits} pieces of {pokemon.PokemonId} for {xpCount}xp");

                await Task.Delay(3000);
            }
        }
        #endregion

        #region AUTO FARMING
        private async Task ExecuteCatchAllNearbyPokemons(Client client)
        {
            var mapObjects = await client.GetMapObjects();

            wildPokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);
            var inventory2 = await client.GetInventory();
            var pokemons2 = inventory2.InventoryDelta.InventoryItems
                .Select(i => i.InventoryItemData?.Pokemon)
                .Where(p => p != null && p?.PokemonId > 0)
                .ToArray();

            foreach (var pokemon in wildPokemons)
            {
                if (Stopping)
                    break;

                await locationManager.update(pokemon.Latitude, pokemon.Longitude);

                string pokemonName;
                if (ClientSettings.Language == "german")
                {
                    string name_english = Convert.ToString(pokemon.PokemonId);
                    var request = (HttpWebRequest)WebRequest.Create("http://boosting-service.de/pokemon/index.php?pokeName=" + name_english);
                    var response = (HttpWebResponse)request.GetResponse();
                    pokemonName = new StreamReader(response.GetResponseStream()).ReadToEnd();
                }
                else
                    pokemonName = Convert.ToString(pokemon.PokemonId);

                await client.UpdatePlayerLocation(pokemon.Latitude, pokemon.Longitude);
                UpdatePlayerLocationOnMap(pokemon.Latitude, pokemon.Longitude);
                UpdateMap();
                var encounterPokemonResponse = await client.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnpointId);
                var pokemonCP = encounterPokemonResponse?.WildPokemon?.PokemonData?.Cp;
                var pokemonIV = Math.Round((double)encounterPokemonResponse?.WildPokemon?.PokemonData.GetIV() * 100);
                CatchPokemonResponse caughtPokemonResponse;
                ColoredConsoleWrite(Color.Green, $"Encounter a {pokemonName} with {pokemonCP} CP and {pokemonIV}% IV");
                do
                {
                    if (ClientSettings.RazzBerryMode == "cp")
                        if (pokemonCP > ClientSettings.RazzBerrySetting)
                            await client.UseRazzBerry(client, pokemon.EncounterId, pokemon.SpawnpointId);
                    if (ClientSettings.RazzBerryMode == "probability")
                        if (encounterPokemonResponse.CaptureProbability.CaptureProbability_.First() < ClientSettings.RazzBerrySetting)
                            await client.UseRazzBerry(client, pokemon.EncounterId, pokemon.SpawnpointId);
                    caughtPokemonResponse = await client.CatchPokemon(pokemon.EncounterId, pokemon.SpawnpointId, pokemon.Latitude, pokemon.Longitude, MiscEnums.Item.ITEM_POKE_BALL, pokemonCP); ; //note: reverted from settings because this should not be part of settings but part of logic
                } while (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchMissed || caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchEscape);

                if (caughtPokemonResponse.Status == CatchPokemonResponse.Types.CatchStatus.CatchSuccess)
                {
                    Color c = Color.LimeGreen;
                    if (pokemonIV >= 80)
                    {
                        c = Color.Yellow;
                    }
                    ColoredConsoleWrite(c, $"We caught a {pokemonName} with {pokemonCP} CP and {pokemonIV}% IV (+{caughtPokemonResponse.Scores.Xp.Sum(a => a)} XP, +{caughtPokemonResponse.Scores.Stardust.Sum(a => a)} Stardust)");
                    foreach (int xp in caughtPokemonResponse.Scores.Xp)
                        TotalExperience += xp;
                    TotalPokemon += 1;
                }
                else
                    ColoredConsoleWrite(Color.Red, $"{pokemonName} with {pokemonCP} CP and {pokemonIV}% IV got away..");


                // I believe a switch is more efficient and easier to read.
                switch (ClientSettings.TransferType)
                {
                    case "Leave Strongest":
                        await TransferAllButStrongestUnwantedPokemon(client);
                        break;
                    case "All":
                        await TransferAllGivenPokemons(client, pokemons2);
                        break;
                    case "Duplicate":
                        await TransferDuplicatePokemon(client);
                        break;
                    case "IV Duplicate":
                        await TransferDuplicateIVPokemon(client);
                        break;
                    case "CP":
                        await TransferAllWeakPokemon(client, ClientSettings.TransferCPThreshold);
                        break;
                    case "IV":
                        await TransferAllGivenPokemons(client, pokemons2, ClientSettings.TransferIVThreshold);
                        break;
                    default:
                        ColoredConsoleWrite(Color.DarkGray, "Transfering pokemon disabled");
                        break;
                }
                
                await Task.Delay(3000);
            }
            wildPokemons = null;
        }

        private async Task ExecuteFarmingPokestopsAndPokemons(Client client)
        {
            var mapObjects = await client.GetMapObjects();

            if (pokeStops == null)
            { 
                FortData[] rawPokeStops = mapObjects.MapCells.SelectMany(i => i.Forts).Where(i => i.Type == FortType.Checkpoint).ToArray();

                if (rawPokeStops == null || rawPokeStops.Length == 0)
                {
                    ColoredConsoleWrite(Color.Red, $"No PokeStops to visit here, please stop the bot and change your location.");
                    return;
                }

                pokeStops = RouteOptimizer.Optimize(rawPokeStops, new LatLong(ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude), pokestopsOverlay);
                wildPokemons = mapObjects.MapCells.SelectMany(i => i.CatchablePokemons);
                if (wildPokemons.Count() > 0)
                {
                    await TryUnban(pokeStops);
                    await locationManager.update(ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude);
                    await ExecuteCatchAllNearbyPokemons(client);
                }
            }
            if (!Stopping)
                ColoredConsoleWrite(Color.Cyan, $"Visiting {pokeStops.Count()} PokeStops");

            foreach (var pokeStop in pokeStops)
            {
                if (Stopping)
                    break;
                
                await locationManager.update(pokeStop.Latitude, pokeStop.Longitude);
                UpdatePlayerLocationOnMap(pokeStop.Latitude, pokeStop.Longitude);
                UpdateMap();

                if (pokeStop.CooldownCompleteTimestampMs <= DateTime.UtcNow.ToUnixTime())
                {
                    var fortInfo = await client.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                    var fortSearch = await client.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                    await TryUnban(pokeStop, fortInfo, fortSearch);

                    StringWriter PokeStopOutput = new StringWriter();
                    PokeStopOutput.Write($"");
                    if (fortInfo.Name != string.Empty)
                        PokeStopOutput.Write("PokeStop: " + fortInfo.Name);
                    if (fortSearch.ExperienceAwarded != 0)
                        PokeStopOutput.Write($", XP: {fortSearch.ExperienceAwarded}");
                    if (fortSearch.GemsAwarded != 0)
                        PokeStopOutput.Write($", Gems: {fortSearch.GemsAwarded}");
                    if (fortSearch.PokemonDataEgg != null)
                        PokeStopOutput.Write($", Eggs: {fortSearch.PokemonDataEgg}");
                    if (GetFriendlyItemsString(fortSearch.ItemsAwarded) != string.Empty)
                        PokeStopOutput.Write($", Items: {GetFriendlyItemsString(fortSearch.ItemsAwarded)} ");
                    ColoredConsoleWrite(Color.Cyan, PokeStopOutput.ToString());

                    if (fortSearch.ExperienceAwarded != 0)
                        TotalExperience += (fortSearch.ExperienceAwarded);

                    pokeStop.CooldownCompleteTimestampMs = DateTime.UtcNow.ToUnixTime() + 300000;
                }

                if (ClientSettings.CatchPokemon)
                    await ExecuteCatchAllNearbyPokemons(client);
            }

            if (!Stopping)
            {
                client.RecycleItems(client);
                await ExecuteFarmingPokestopsAndPokemons(client);
            }
        }

        private async Task TryUnban(IEnumerable<FortData> pokeStops)
        {
            await TryUnban(pokeStops.Where(f => f.CooldownCompleteTimestampMs <= DateTime.UtcNow.ToUnixTime()).FirstOrDefault());
        }

        private async Task TryUnban(FortData pokeStop)
        {
            await locationManager.update(pokeStop.Latitude, pokeStop.Longitude);
            var fortInfo = await botClient.GetFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
            var fortSearch = await botClient.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
            await TryUnban(pokeStop, fortInfo, fortSearch);
        }

        private async Task TryUnban(FortData pokeStop, FortDetailsResponse fortInfo, FortSearchResponse fortSearch)
        {
            if (fortSearch.ExperienceAwarded == 0)
            {
                ColoredConsoleWrite(Color.Pink, "You get softbanned. Starting attemp to unban...");
                await Task.Delay(1000);
                for (int i = 1; i <= 50; i++)
                {
                    fortSearch = await botClient.SearchFort(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);
                    if (fortSearch.ExperienceAwarded == 0)
                    {
                        ColoredConsoleWrite(Color.Pink, "Attempt: " + i);
                    }
                    else
                    {
                        ColoredConsoleWrite(Color.Pink, "Yeah! You are now unbanned! Total attempts: " + i);
                        break;
                    }
                }
                if (fortSearch.ExperienceAwarded == 0)
                {
                    ColoredConsoleWrite(Color.Pink, "Failed. Will try again on the next PokeStop.");
                }
            }
        }

        #endregion

        #region MAP
        private void InitializeMap()
        {
            playerMarker.Position = new PointLatLng(ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude);
            gMapControl1.Position = playerMarker.Position;

            searchAreaOverlay.Polygons.Clear();
            S2GMapDrawer.DrawS2Cells(S2Helper.GetNearbyCellIds(ClientSettings.DefaultLongitude, ClientSettings.DefaultLatitude), searchAreaOverlay);
        }

        private void UpdatePlayerLocationOnMap(double latitude, double longitude)
        {
            synchronizationContext.Post(new SendOrPostCallback(o =>
            {
                playerMarker.Position = (PointLatLng)o;

                searchAreaOverlay.Polygons.Clear();

            }), new PointLatLng(latitude, longitude));

            ColoredConsoleWrite(Color.DarkGray, $"Moving player location to Lat: {latitude}, Lng: {longitude}");
        }

        private void UpdateMap()
        {
            synchronizationContext.Post(new SendOrPostCallback(o =>
            {
                pokestopsOverlay.Markers.Clear();
                List<PointLatLng> routePoint = new List<PointLatLng>();
                foreach (var pokeStop in pokeStops)
                {
                    GMarkerGoogleType type = GMarkerGoogleType.blue_small;
                    if (pokeStop.CooldownCompleteTimestampMs > DateTime.UtcNow.ToUnixTime())
                    {
                        type = GMarkerGoogleType.gray_small;
                    }
                    var pokeStopLoc = new PointLatLng(pokeStop.Latitude, pokeStop.Longitude);
                    var pokestopMarker = new GMarkerGoogle(pokeStopLoc, type);
                    //pokestopMarker.ToolTipMode = MarkerTooltipMode.OnMouseOver;
                    //pokestopMarker.ToolTip = new GMapBaloonToolTip(pokestopMarker);
                    pokestopsOverlay.Markers.Add(pokestopMarker);

                    routePoint.Add(pokeStopLoc);
                }
                pokestopsOverlay.Routes.Clear();
                pokestopsOverlay.Routes.Add(new GMapRoute(routePoint, "Walking Path"));


                pokemonsOverlay.Markers.Clear();
                if (wildPokemons != null)
                {
                    foreach (var pokemon in wildPokemons)
                    {
                        var pokemonMarker = new GMarkerGoogle(new PointLatLng(pokemon.Latitude, pokemon.Longitude),
                            GMarkerGoogleType.red_small);
                        pokemonsOverlay.Markers.Add(pokemonMarker);
                    }
                }

                searchAreaOverlay.Polygons.Clear();
                S2GMapDrawer.DrawS2Cells(S2Helper.GetNearbyCellIds(ClientSettings.DefaultLongitude, ClientSettings.DefaultLatitude), searchAreaOverlay);
            }), null);
        }
        #endregion

        #region AUTO TRANSFER
        private async Task TransferAllButStrongestUnwantedPokemon(Client client)
        {
            var unwantedPokemonTypes = new List<PokemonId>();
            for (int i = 1; i <= 151; i++)
            {
                unwantedPokemonTypes.Add((PokemonId)i);
            }
            
            var inventory = await client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems
                .Select(i => i.InventoryItemData?.Pokemon)
                .Where(p => p != null && p?.PokemonId > 0)
                .ToArray();

            foreach (var unwantedPokemonType in unwantedPokemonTypes)
            {
                var pokemonOfDesiredType = pokemons.Where(p => p.PokemonId == unwantedPokemonType)
                    .OrderByDescending(p => p.Cp)
                    .ToList();

                var unwantedPokemon =
                    pokemonOfDesiredType.Skip(1) // keep the strongest one for potential battle-evolving
                        .ToList();
                
                await TransferAllGivenPokemons(client, unwantedPokemon);
            }
        }

        private async Task TransferAllGivenPokemons(Client client, IEnumerable<PokemonData> unwantedPokemons, float keepPerfectPokemonLimit = 0.8f)
        {
            foreach (var pokemon in unwantedPokemons)
            {
                if (pokemon.GetIV() >= keepPerfectPokemonLimit) continue;
                ColoredConsoleWrite(Color.White, $"Pokemon {pokemon.PokemonId} with {pokemon.Cp} CP has IV percent less than {keepPerfectPokemonLimit * 100}%");

                if (pokemon.Favorite == 0)
                {
                    var transferPokemonResponse = await client.TransferPokemon(pokemon.Id);

                    /*
                    ReleasePokemonOutProto.Status {
                        UNSET = 0;
                        SUCCESS = 1;
                        POKEMON_DEPLOYED = 2;
                        FAILED = 3;
                        ERROR_POKEMON_IS_EGG = 4;
                    }*/
                    string pokemonName;
                    if (ClientSettings.Language == "german")
                    {
                        // Dont really need to print this do we? youll know if its German or not
                        //ColoredConsoleWrite(Color.DarkCyan, "german");
                        string name_english = Convert.ToString(pokemon.PokemonId);
                        var request = (HttpWebRequest)WebRequest.Create("http://boosting-service.de/pokemon/index.php?pokeName=" + name_english);
                        var response = (HttpWebResponse)request.GetResponse();
                        pokemonName = new StreamReader(response.GetResponseStream()).ReadToEnd();
                    }
                    else
                        pokemonName = Convert.ToString(pokemon.PokemonId);
                    if (transferPokemonResponse.Status == 1)
                    {
                        ColoredConsoleWrite(Color.Magenta, $"Transferred {pokemonName} with {pokemon.Cp} CP");
                    }
                    else
                    {
                        var status = transferPokemonResponse.Status;

                        ColoredConsoleWrite(Color.Red, $"Somehow failed to transfer {pokemonName} with {pokemon.Cp} CP. " +
                                                 $"ReleasePokemonOutProto.Status was {status}");
                    }

                    await Task.Delay(3000);
                }
            }
        }

        private async Task TransferDuplicatePokemon(Client client)
        {

            //ColoredConsoleWrite(ConsoleColor.White, $"Check for duplicates");
            var inventory = await client.GetInventory();
            var allpokemons =
                inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                    .Where(p => p != null && p?.PokemonId > 0);

            var dupes = allpokemons.OrderBy(x => x.Cp).Select((x, i) => new { index = i, value = x })
                .GroupBy(x => x.value.PokemonId)
                .Where(x => x.Skip(1).Any());

            for (var i = 0; i < dupes.Count(); i++)
            {
                for (var j = 0; j < dupes.ElementAt(i).Count() - 1; j++)
                {
                    var dubpokemon = dupes.ElementAt(i).ElementAt(j).value;
                    if (dubpokemon.Favorite == 0)
                    {
                        var transfer = await client.TransferPokemon(dubpokemon.Id);
                        string pokemonName;
                        if (ClientSettings.Language == "german")
                        {
                            string name_english = Convert.ToString(dubpokemon.PokemonId);
                            var request = (HttpWebRequest)WebRequest.Create("http://boosting-service.de/pokemon/index.php?pokeName=" + name_english);
                            var response = (HttpWebResponse)request.GetResponse();
                            pokemonName = new StreamReader(response.GetResponseStream()).ReadToEnd();
                        }
                        else
                            pokemonName = Convert.ToString(dubpokemon.PokemonId);
                        ColoredConsoleWrite(Color.DarkGreen,
                            $"Transferred {pokemonName} with {dubpokemon.Cp} CP (Highest is {dupes.ElementAt(i).Last().value.Cp})");

                    }
                }
            }
        }

        private async Task TransferDuplicateIVPokemon(Client client)
        {

            //ColoredConsoleWrite(ConsoleColor.White, $"Check for duplicates");
            var inventory = await client.GetInventory();
            var allpokemons =
                inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                    .Where(p => p != null && p?.PokemonId > 0);

            var dupes = allpokemons.OrderBy(x => x.GetIV()).Select((x, i) => new { index = i, value = x })
                .GroupBy(x => x.value.PokemonId)
                .Where(x => x.Skip(1).Any());

            for (var i = 0; i < dupes.Count(); i++)
            {
                for (var j = 0; j < dupes.ElementAt(i).Count() - 1; j++)
                {
                    var dubpokemon = dupes.ElementAt(i).ElementAt(j).value;
                    if (dubpokemon.Favorite == 0)
                    {
                        var transfer = await client.TransferPokemon(dubpokemon.Id);
                        string pokemonName;
                        if (ClientSettings.Language == "german")
                        {
                            string name_english = Convert.ToString(dubpokemon.PokemonId);
                            var request = (HttpWebRequest)WebRequest.Create("http://boosting-service.de/pokemon/index.php?pokeName=" + name_english);
                            var response = (HttpWebResponse)request.GetResponse();
                            pokemonName = new StreamReader(response.GetResponseStream()).ReadToEnd();
                        }
                        else
                            pokemonName = Convert.ToString(dubpokemon.PokemonId);
                        ColoredConsoleWrite(Color.DarkGreen,
                            $"Transferred {pokemonName} with {Math.Round(dubpokemon.GetIV() * 100)}% IV (Highest is {Math.Round(dupes.ElementAt(i).Last().value.GetIV() * 100)}% IV)");

                    }
                }
            }
        }

        private async Task TransferAllWeakPokemon(Client client, int cpThreshold)
        {
            //ColoredConsoleWrite(ConsoleColor.White, $"Firing up the meat grinder");

            PokemonId[] doNotTransfer = new[] //these will not be transferred even when below the CP threshold
            { // DO NOT EMPTY THIS ARRAY
                //PokemonId.Pidgey,
                //PokemonId.Rattata,
                //PokemonId.Weedle,
                //PokemonId.Zubat,
                //PokemonId.Caterpie,
                //PokemonId.Pidgeotto,
                //PokemonId.NidoranFemale,
                //PokemonId.Paras,
                //PokemonId.Venonat,
                //PokemonId.Psyduck,
                //PokemonId.Poliwag,
                //PokemonId.Slowpoke,
                //PokemonId.Drowzee,
                //PokemonId.Gastly,
                //PokemonId.Goldeen,
                //PokemonId.Staryu,
                PokemonId.Magikarp,
                PokemonId.Eevee//,
                //PokemonId.Dratini
            };

            var inventory = await client.GetInventory();
            var pokemons = inventory.InventoryDelta.InventoryItems
                                .Select(i => i.InventoryItemData?.Pokemon)
                                .Where(p => p != null && p?.PokemonId > 0)
                                .ToArray();

            //foreach (var unwantedPokemonType in unwantedPokemonTypes)
            {
                List<PokemonData> pokemonToDiscard;
                if (doNotTransfer.Count() != 0)
                    pokemonToDiscard = pokemons.Where(p => !doNotTransfer.Contains(p.PokemonId) && p.Cp < cpThreshold).OrderByDescending(p => p.Cp).ToList();
                else
                    pokemonToDiscard = pokemons.Where(p => p.Cp < cpThreshold).OrderByDescending(p => p.Cp).ToList();


                //var unwantedPokemon = pokemonOfDesiredType.Skip(1) // keep the strongest one for potential battle-evolving
                //                                          .ToList();
                ColoredConsoleWrite(Color.Gray, $"Grinding {pokemonToDiscard.Count} pokemon below {cpThreshold} CP.");
                await TransferAllGivenPokemons(client, pokemonToDiscard);

            }

            ColoredConsoleWrite(Color.Gray, $"Finished grinding all the meat");
        }
        #endregion

        #region MISC
        private static string CallAPI(string elem, double lat, double lon)
        {
            using (XmlReader reader = XmlReader.Create(@"http://api.geonames.org/findNearby?lat=" + lat + "&lng=" + lon + "&username=demo"))
            {
                while (reader.Read())
                {
                    if (reader.IsStartElement())
                    {
                        switch (elem)
                        {
                            case "country":
                                if (reader.Name == "countryName")
                                {
                                    return reader.ReadString();
                                }
                                break;

                            case "place":
                                if (reader.Name == "toponymName")
                                {
                                    return reader.ReadString();
                                }
                                break;
                            default:
                                return "N/A";
                                break;
                        }
                    }
                }
            }
            return "Error";
        }

        public void SetStatusText(string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(SetStatusText), text);
                return;
            }

            statusLabel.Text = text;
        }

        private string GetFriendlyItemsString(IEnumerable<FortSearchResponse.Types.ItemAward> items)
        {
            var enumerable = items as IList<FortSearchResponse.Types.ItemAward> ?? items.ToList();

            if (!enumerable.Any())
                return string.Empty;

            return enumerable.GroupBy(i => i.ItemId)
                    .Select(kvp => new { ItemName = kvp.Key.ToString().Substring(4), Amount = kvp.Sum(x => x.ItemCount) })
                    .Select(y => $"{y.Amount}x {y.ItemName}")
                    .Aggregate((a, b) => $"{a}, {b}");
        }

        public async Task PrintLevel(Client client)
        {
            var inventory = await client.GetInventory();
            var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.PlayerStats).ToArray();
            foreach (var v in stats)
                if (v != null)
                {
                    int XpDiff = GetXpDiff(client, v.Level);
                    if (ClientSettings.LevelOutput == "time")
                        ColoredConsoleWrite(Color.Yellow, $"Current Level: " + v.Level + " (" + (v.Experience - XpDiff) + "/" + (v.NextLevelXp - XpDiff) + ")");
                    else if (ClientSettings.LevelOutput == "levelup")
                        if (Currentlevel != v.Level)
                        {
                            Currentlevel = v.Level;
                            ColoredConsoleWrite(Color.Magenta, $"Current Level: " + v.Level + ". XP needed for next Level: " + (v.NextLevelXp - v.Experience));
                        }
                }
            if (ClientSettings.LevelOutput == "levelup")
                await Task.Delay(1000);
            else
                await Task.Delay(ClientSettings.LevelTimeInterval * 1000);
            PrintLevel(client);
        }

        public void ColoredConsoleWrite(Color color, string text)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<Color, string>(ColoredConsoleWrite), color, text);
                return;
            }

            logTextBox.Select(logTextBox.Text.Length, 1); // Reset cursor to last

            string textToAppend = "[" + DateTime.Now.ToString("HH:mm:ss tt") + "] " + text + "\r\n";
            logTextBox.SelectionColor = color;
            logTextBox.AppendText(textToAppend);

            object syncRoot = new object();
            lock (syncRoot) // Added locking to prevent text file trying to be accessed by two things at the same time
            {
                File.AppendAllText(AppDomain.CurrentDomain.BaseDirectory + @"\Logs.txt", "[" + DateTime.Now.ToString("HH:mm:ss tt") + "] " + text + "\n");
            }
        }

        public void ConsoleClear()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(ConsoleClear));
                return;
            }

            logTextBox.Clear();
        }

        public static string _getSessionRuntimeInTimeFormat()
        {
            return (DateTime.Now - InitSessionDateTime).ToString(@"dd\.hh\:mm\:ss");
        }

        public async Task ConsoleLevelTitle(string Username, Client client)
        {
            var inventory = await client.GetInventory();
            var stats = inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.PlayerStats).ToArray();
            var profile = await client.GetProfile();
            foreach (var v in stats)
                if (v != null)
                {
                    int XpDiff = GetXpDiff(client, v.Level);
                    SetStatusText(string.Format(Username + " | Level: {0:0} - ({2:0} / {3:0}) | Runtime {1} | Stardust: {4:0}", v.Level, _getSessionRuntimeInTimeFormat(), (v.Experience - v.PrevLevelXp - XpDiff), (v.NextLevelXp - v.PrevLevelXp - XpDiff), profile.Profile.Currency.ToArray()[1].Amount) + " | XP/Hour: " + Math.Round(TotalExperience / GetRuntime()) + " | Pokemon/Hour: " + Math.Round(TotalPokemon / GetRuntime()));
                }
            await Task.Delay(1000);
            ConsoleLevelTitle(Username, client);
        }

        private void Client_OnConsoleWrite(ConsoleColor color, string message)
        {
            Color c = Color.White;
            switch (color)
            {
                case ConsoleColor.Green:
                    c = Color.Green;
                    break;
                case ConsoleColor.DarkCyan:
                    c = Color.DarkCyan;
                    break;
            }
            ColoredConsoleWrite(c, message);
        }

        public static int GetXpDiff(Client client, int Level)
        {
            switch (Level)
            {
                case 1: return 0;
                case 2: return 1000;
                case 3: return 2000;
                case 4: return 3000;
                case 5: return 4000;
                case 6: return 5000;
                case 7: return 6000;
                case 8: return 7000;
                case 9: return 8000;
                case 10: return 9000;
                case 11: return 10000;
                case 12: return 10000;
                case 13: return 10000;
                case 14: return 10000;
                case 15: return 15000;
                case 16: return 20000;
                case 17: return 20000;
                case 18: return 20000;
                case 19: return 25000;
                case 20: return 25000;
                case 21: return 50000;
                case 22: return 75000;
                case 23: return 100000;
                case 24: return 125000;
                case 25: return 150000;
                case 26: return 190000;
                case 27: return 200000;
                case 28: return 250000;
                case 29: return 300000;
                case 30: return 350000;
                case 31: return 500000;
                case 32: return 500000;
                case 33: return 750000;
                case 34: return 1000000;
                case 35: return 1250000;
                case 36: return 1500000;
                case 37: return 2000000;
                case 38: return 2500000;
                case 39: return 1000000;
                case 40: return 1000000;
            }
            return 0;
        }

        public static double GetRuntime()
        {
            return ((DateTime.Now - TimeStarted).TotalSeconds) / 3600;
        }
        #endregion

        #region MAIN UI EVENT
        private void MainForm_Load(object sender, EventArgs e)
        {
            gMapControl1.MapProvider = GoogleMapProvider.Instance;
            gMapControl1.Manager.Mode = AccessMode.ServerOnly;
            GMapProvider.WebProxy = null;
            gMapControl1.Position = new PointLatLng(ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude);
            gMapControl1.DragButton = MouseButtons.Left;
            gMapControl1.IgnoreMarkerOnMouseWheel = true;

            gMapControl1.MinZoom = 1;
            gMapControl1.MaxZoom = 20;
            gMapControl1.Zoom = 15;

            gMapControl1.Overlays.Add(searchAreaOverlay);
            gMapControl1.Overlays.Add(pokestopsOverlay);
            gMapControl1.Overlays.Add(pokemonsOverlay);
            gMapControl1.Overlays.Add(playerOverlay);

            playerMarker = new GMarkerGoogle(new PointLatLng(ClientSettings.DefaultLatitude, ClientSettings.DefaultLongitude),
                GMarkerGoogleType.orange_small);
            playerOverlay.Markers.Add(playerMarker);

            InitializePokemonForm();
        }

        private void logTextBox_TextChanged(object sender, EventArgs e)
        {
            logTextBox.SelectionStart = logTextBox.Text.Length;
            logTextBox.ScrollToCaret();
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SettingsForm settingsForm = new SettingsForm();
            settingsForm.Show();
        }

        private void startStopBotToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!IsBotStarted)
            {
                StartBot();
            }
            else
            {
                StopBot();
            }
        }

        private void showAllToolStripMenuItem3_Click(object sender, EventArgs e)
        {
        }

        private void statsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // todo: add player stats later
        }

        private async void useLuckyEggToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (botClient != null)
            {
                try
                {
                    IEnumerable<Item> myItems = await botClient.GetItems(botClient);
                    IEnumerable<Item> LuckyEggs = myItems.Where(i => (ItemId)i.Item_ == ItemId.ItemLuckyEgg);
                    Item LuckyEgg = LuckyEggs.FirstOrDefault();
                    if (LuckyEgg != null)
                    {
                        var useItemXpBoostRequest = await botClient.UseItemXpBoost(ItemId.ItemLuckyEgg);
                        ColoredConsoleWrite(Color.Green, $"Using a Lucky Egg, we have {LuckyEgg.Count} left.");
                        ColoredConsoleWrite(Color.Yellow, $"Lucky Egg Valid until: {DateTime.Now.AddMinutes(30).ToString()}");

                        var stripItem = sender as ToolStripMenuItem;
                        stripItem.Enabled = false;
                        await Task.Delay(30000);
                        stripItem.Enabled = true;
                    }
                    else
                    {
                        ColoredConsoleWrite(Color.Red, $"You don't have any Lucky Egg to use.");
                    }
                }
                catch (Exception ex)
                {
                    ColoredConsoleWrite(Color.Red, $"Unhandled exception in using lucky egg: {ex}");
                }
            }
            else
            {
                ColoredConsoleWrite(Color.Red, "Please start the bot before trying to use a lucky egg.");
            }
        }

        private void showAllToolStripMenuItem2_Click(object sender, EventArgs e)
        {

        }

        private void todoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SettingsForm settingsForm = new SettingsForm();
            settingsForm.Show();
        }
        #endregion

        #region POKEMON LIST
        private IEnumerable<PokemonFamily> families;
        private static int TransferButtonColumnIndex = 7;
        private static int PowerUpButtonColumnIndex = PowerUpButtonColumnIndex + 1;
        private static int EvolveButtonColumnIndex = EvolveButtonColumnIndex + 1;

        private void InitializePokemonForm()
        {
            objectListView1.ButtonClick += PokemonListButton_Click;

            pkmnName.ImageGetter = delegate (object rowObject)
            {
                PokemonData pokemon = (PokemonData)rowObject;
                
                String key = pokemon.PokemonId.ToString();
                
                if (!objectListView1.SmallImageList.Images.ContainsKey(key))
                {
                    Image largeImage = GetPokemonImage((int)pokemon.PokemonId);
                    objectListView1.SmallImageList.Images.Add(key, largeImage);
                    objectListView1.LargeImageList.Images.Add(key, largeImage);
                }
                return key;
            };

            objectListView1.CellToolTipShowing += delegate (object sender, ToolTipShowingEventArgs args)
            {
                PokemonData pokemon = (PokemonData)args.Model;

                var family = families
                        .Where(i => (int)i.FamilyId <= (int)pokemon.PokemonId)
                        .First();
                args.Text = $"You have {family.Candy} {((PokemonId)((int)family.FamilyId)).ToString()} Candy";
            };

            objectListView1.FormatRow += delegate (object sender, FormatRowEventArgs args) {
                args.Item.Text = args.RowIndex.ToString();
            };
        }

        private static Image GetPokemonImage(int pokemonId)
        {
            var Sprites = AppDomain.CurrentDomain.BaseDirectory + "Sprites\\";
            string location = Sprites + pokemonId + ".png";
            if (!Directory.Exists(Sprites))
                Directory.CreateDirectory(Sprites);
            if (!File.Exists(location))
            {
                WebClient wc = new WebClient();
                wc.DownloadFile("http://pokeapi.co/media/sprites/pokemon/" + pokemonId + ".png", @location);
            }
            return Image.FromFile(location);
        }

        private async void ReloadPokemonList()
        {
            button1.Enabled = false;
            objectListView1.Enabled = false;

            if (pokemonListClient == null)
            {
                pokemonListClient = new Client(ClientSettings);
            }
            try
            {
                if (!pokemonListClient.HasServerSet())
                {
                    switch (ClientSettings.AuthType)
                    {
                        case AuthType.Ptc:
                            ColoredConsoleWrite(Color.Green, "Login Type: Pokemon Trainers Club");
                            break;
                        case AuthType.Google:
                            ColoredConsoleWrite(Color.Green, "Login Type: Google");
                            break;
                    }
                    await pokemonListClient.Login();
                    await pokemonListClient.SetServer();
                }
                var inventory = await pokemonListClient.GetInventory();
                var pokemons =
                    inventory.InventoryDelta.InventoryItems.Select(i => i.InventoryItemData?.Pokemon)
                        .Where(p => p != null && p?.PokemonId > 0).OrderByDescending(key => key.Cp);

                families = inventory.InventoryDelta.InventoryItems
                    .Select(i => i.InventoryItemData?.PokemonFamily)
                    .Where(p => p != null && (int)p?.FamilyId > 0)
                    .OrderByDescending(p => (int)p.FamilyId);
                
                int prevTopItem = objectListView1.TopItemIndex;
                objectListView1.SetObjects(pokemons);
                objectListView1.TopItemIndex = prevTopItem;
            }
            catch (Exception ex) { ColoredConsoleWrite(Color.Red, ex.ToString()); pokemonListClient = null; ReloadPokemonList(); }

            button1.Enabled = true;
            objectListView1.Enabled = true;
        }

        private void PokemonListButton_Click(object sender, CellClickEventArgs e)
        {
            try
            {
                PokemonData pokemon = (PokemonData)e.Model;
                if (e.ColumnIndex == TransferButtonColumnIndex)
                {
                    TransferPokemon(pokemon);
                }
                else if (e.ColumnIndex == PowerUpButtonColumnIndex)
                {
                    PowerUpPokemon(pokemon);
                }
                else if (e.ColumnIndex == EvolveButtonColumnIndex)
                {
                    EvolvePokemon(pokemon);
                }
            }
            catch (Exception ex) { ColoredConsoleWrite(Color.Red, ex.ToString()); pokemonListClient = null; ReloadPokemonList(); }
        }

        private async void TransferPokemon(PokemonData pokemon)
        {
            if (MessageBox.Show($"Are you sure you want to transfer {pokemon.PokemonId.ToString()} with {pokemon.Cp} CP?", "Confirmation", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                var transferPokemonResponse = await pokemonListClient.TransferPokemon(pokemon.Id);

                if (transferPokemonResponse.Status == 1)
                {
                    ColoredConsoleWrite(Color.Magenta, $"{pokemon.PokemonId} was transferred. {transferPokemonResponse.CandyAwarded} candy awarded");
                    ReloadPokemonList();
                }
                else
                {
                    ColoredConsoleWrite(Color.Magenta, $"{pokemon.PokemonId} could not be transferred");
                }
            }
        }

        private async void PowerUpPokemon(PokemonData pokemon)
        {
            var evolvePokemonResponse = await pokemonListClient.PowerUp(pokemon.Id);

            if (evolvePokemonResponse.Result == 1)
            {
                ColoredConsoleWrite(Color.Magenta, $"{pokemon.PokemonId} successfully upgraded.");
                ReloadPokemonList();
            }
            else
            {
                ColoredConsoleWrite(Color.Magenta, $"{pokemon.PokemonId} could not be upgraded");
            }
        }

        private async void EvolvePokemon(PokemonData pokemon)
        {
            var evolvePokemonResponse = await pokemonListClient.EvolvePokemon(pokemon.Id);

            if (evolvePokemonResponse.Result == 1)
            {
                ColoredConsoleWrite(Color.Magenta, $"{pokemon.PokemonId} successfully evolved into {evolvePokemonResponse.EvolvedPokemon.PokemonType}\n{evolvePokemonResponse.ExpAwarded} experience awarded\n{evolvePokemonResponse.CandyAwarded} candy awarded");
                ReloadPokemonList();
            }
            else
            {
                ColoredConsoleWrite(Color.Magenta, $"{pokemon.PokemonId} could not be evolved");
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            ReloadPokemonList();
        }
        #endregion
    }
}
