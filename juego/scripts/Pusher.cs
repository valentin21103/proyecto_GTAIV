// ============================================================================
//  PUSHER 3.60  -  drug dealing for GTA IV Complete Edition (IV / TLAD / TBoGT)
//  Standalone mod. Single .cs script: drop this file in  scripts\  and the
//  Pusher folder next to it.
//
//  scripts\Pusher\
//      Pusher.ini                  settings, odds, prices, animations, dealers (made on first run)
//      Pusher.log                  errors, if any
//      stash.dat                   what each protagonist is carrying + dealer relationships
//      locations.txt               your dealers:  (x, y, z, heading), (ped model)
//                                  empty model  ()  = a door dealer: no ped, you deal at the spot
//      Icons\coke_bag.png coke_pack.png heroin.png pills.png   drug icons shown in the menus
//      Icons\RB.png DpadLeft.png DpadRight.png   optional button icons (text boxes if missing)
//      Icons\coke_pack_pickup.png heroin_pickup.png pills_pickup.png   shown above the minimap when you
//                                  get drugs without buying them (robbed dealer, thief's drop, DynamicScenarios)
//      IV\  TLAD\  TBoGT\          PLAYER voice lines, one random .wav per event:
//          BuyBegin   you press Buy Drugs on a dealer
//          BuyDuring  every 5th press in the dealer menu
//          BuyAfter   2 s after the hand-over with the dealer
//          SellBegin  you press Sell Drugs on a buyer
//          Sell       during the hand-over, once the buyer has what he wanted
//          SellAfter  2 s after the Sell line has finished (60%)
//          Angry      after being punched / robbed / stung
//          AngryKill  once the NPC who wronged you is dead
//          Robbed     when your hands go up
//          Search       you get out of the car / off the bike when the cop tells you to
//          SearchRefuse you refuse to put your hands on your head (then 2 stars)
//          SearchComply you put your hands up and let him take you in
//  NPCs (dealers, buyers, cops) only ever talk with their own in-game voice.
//
//  COMPATIBILITY BRIDGE (other scripts read these with AppDomain.CurrentDomain.GetData)
//      "Pusher.Version"      int
//      "Pusher.Alive"        int                Game.GameTime, stamped every frame (stale = not running)
//      "Pusher.GtrLink"      bool               GreetTauntRobCompat from Pusher.ini
//      "Pusher.Busy"         bool               a Pusher menu or scene is running: hide your UI
//      "Pusher.Carrying"     bool               the player holds any drugs
//      "Pusher.IsBuyer"      Predicate<Ped>     this NPC can be sold to
//      "Pusher.IsDealer"     Predicate<Ped>     this NPC is one of Pusher's dealers
//      "Pusher.OpenSell"     Action<Ped>        opens the Coke / Heroin / Pills tree on that NPC
//      "Pusher.OpenSellAs"   Action<Ped,string> same, with your own title (the faction name)
//      "Pusher.OpenDealer"   Action<Ped>        GreetTauntRob: Buy Drugs on a dealer (locked on)
//      "Pusher.RobDealerStart" Action<Ped>      GreetTauntRob: gunpoint robbery of a dealer begins
//      "Pusher.DealerDrop"   Action<Ped>        GreetTauntRob: the robbed dealer drops cash + stash now
//      "Pusher.BeginSearch2" Func<Ped,Ped,Vehicle,Vehicle,bool>  StopRightThere: door cop, partner (or
//                                               null), the player's vehicle, the squad car. Rolls
//                                               SearchChance; true = Pusher runs the search
//      "Pusher.BeginSearch"  Func<Ped,Vehicle,bool>   older form of the same
//      "Pusher.SearchState"  int                0 none, 1 running, 2 nothing found, the player is back
//                                               in his vehicle (carry on with the ticket), 3 Pusher
//                                               keeps the officers (refused / arrested / the ride),
//                                               4 the player drove off before getting out
//      "Pusher.SrtLink"      bool               StopRightThereCompat from Pusher.ini
//      "Pusher.GiveMaxDrugs" Func<bool>         DynamicScenarios: the player stole a drug shipment - fill every
//                                               drug to its Max and show the three pickup icons
//  The dialogue script announces itself with
//      AppDomain.CurrentDomain.SetData("GreetTauntRob.PusherCompat", true);
//  and stamps "GreetTauntRob.Alive" (Game.GameTime) every frame. Only while both
//  are present and fresh does Pusher leave the lock-on UI to it; otherwise each
//  script runs on its own.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GTA;

public class Pusher : Script
{
    // ------------------------------------------------------------------ constants
    const int VERSION = 61;
    const int COKE = 0, HEROIN = 1, PILLS = 2;
    const int KIND_NONE = 0, KIND_BUSINESS = 1, KIND_HOOKER = 2, KIND_BUM = 3, KIND_GANG = 4, KIND_OTHER = 5;
    const int M_NONE = 0, M_DEALER = 1, M_BUYER = 2, M_SELL = 3;
    const int S_NONE = 0, S_DEAL = 1, S_PUNCH = 2, S_ROB = 3, S_SEARCH = 4, S_BUY = 5;
    const int S_DEALROB = 9;                                  // robbing a dealer who gives up
    const int S_APPROACH = 10;                                // a dealer / buyer walks up to the player
    const int B_SELL = 0, B_D0 = 1, B_ROB = 4;       // B_D0 + drug = that drug's button
    const int B_COMPLY = 5;                          // the officer's choice: comply (RB, the player is locked then)
    const int CH_PLAYER = 0, CH_NPC = 1;

    static readonly string[] DrugName = { "Coke", "Heroin", "Pills" };
    static readonly string[] DrugIconFile = { "coke_bag.png", "heroin.png", "pills.png" };

    // "3g" / "3 pills"
    static string Amount(int drug, int q) { return drug == PILLS ? q + (q == 1 ? " pill" : " pills") : q + "g"; }
    static string Unit(int drug) { return drug == PILLS ? "pill" : "g"; }

    // ------------------------------------------------------------------ small data classes
    class Dealer
    {
        public string Name;
        public Vector3 Pos;
        public float[] Mult = new float[3];
        public Ped Ped;
        public Blip Blip;
        public int NextSpawnTry;
        public int LastGreet;
        public string Model = "";            // ped model ("" = DealerModel from the ini)
        public float Heading = -1f;          // -1 = random
        public bool Exact;                   // from locations.txt: stands exactly where saved
        public bool Door;                    // no ped: the deal happens at a door
        public bool VisitCounted;            // this visit already counted toward regular prices
        public int IdleAt, TalkAt;           // idle stance upkeep / talk gesture timing
        public bool WasBusy;                 // was dealing with the player last tick
        public int[] Stock = new int[3];     // what he has left of each drug
        public int[] RestockAt = new int[3]; // Environment.TickCount when an empty drug comes back (0 = not empty)
        public bool StockSet;
        public Vector3 Home;                 // where he stands (walks back here after a deal)
        public bool HasHome, Returning;
        public int ReturnSince;
        public int LastBuyBegin;
        public int RoomCheckAt;
        public bool Sells(int drug) { return true; }
    }

    class Offer
    {
        public int Kind;
        public int[] Qty = new int[3];
        public int[] Price = new int[3];
        public bool Done;
        public bool Bulk;                    // wants BulkAmount of a drug at once
        public int Born;
        public int[] Sold = new int[3];      // sold to him so far this visit
        public int Units, Paid;
        public bool Risky;                   // trouble already rolled
        public int MaxWant() { int m = 0; for (int i = 0; i < 3; i++) if (Qty[i] > m) m = Qty[i]; return m; }
    }

    class Thief          // an NPC running off with the player's money and / or drugs
    {
        public Ped Ped;
        public int Cash;
        public int[] Drugs = new int[3];
        public int Since;
        public GTA.Object Prop;      // what he snatched, in his hand while he runs
        public bool Fighting;        // you went for him: he fights back with what he has
        public int FightAt;
    }

    class Loot           // the cash pickup a dead thief leaves behind (drugs come back with it)
    {
        public Pickup Pickup;
        public Vector3 Pos;
        public int[] Drugs = new int[3];
        public int Born;
        public bool Dealer;      // a robbed / killed dealer's drop: all three drug icons always show when it is picked up
    }
    const int DealerLootDrugs = 10;   // a robbed or killed dealer's cash drop also gives 10 of each drug

    class Held           // an NPC Pusher keeps hold of for a while, then hands back to the game
    {
        public Ped Ped;
        public int Until;
    }

    class Squad
    {
        public Vehicle Car;
        public Ped Driver, Partner;
        public int Born;
    }

    class Btn
    {
        public string Name = "";
        public ushort Mask;
        public bool Trigger;                 // RT (an analog trigger, not a button bit)
        public Keys Key = Keys.None;
        public bool Down, Pressed;
        public string Label = "";
        public Texture Icon;
    }

    class Voice
    {
        public IntPtr Handle, Header, Data;
        public int Channel, EndsAt;
    }

    class Speech
    {
        public Ped Ped;
        public string[] Lines;
        public int Index, Next;
        public bool Cop, Retried;            // cops get one retry with a cop voice bank
        public bool Started;
        public int Born;
    }

    class Delayed
    {
        public int At, Born;
        public string Folder;
    }

    // ------------------------------------------------------------------ settings (Pusher.ini)
    int[] maxCarry = { 30, 20, 60 };          // coke g, heroin g, pills
    // ---- prices
    int[] buyPrice = { 250, 285, 145 };       // a dealer's price for one pack (BuyPerPress: 5g coke, 5g heroin, 5 pills)
    int[] regularPrice = { 230, 250, 115 };   // the same pack once you are a regular
    int visitsForDiscount = 10;               // visits to the same dealer to become a regular
    int[] sellPrice = { 65, 80, 50 };         // what a buyer pays per gram / pill
    int bulkBuyerChance = 20, bulkAmount = 10;
    int bulkDiscount = 5;                     // a bulk buyer pays this % less than BulkAmount x the normal price
    int[] bulkSellPrice = new int[3];         // worked out from the two above
    int[] buyStep = { 5, 5, 5 };              // one pack: bought per button press at a dealer
    int[] sellMin = { 1, 1, 1 }, sellMax = { 9, 9, 9 };     // what a normal buyer wants of a drug
    int bigOrderFrom = 7, bigOrderRobChance = 30;           // wants this much or more of a drug: this % he tries to rob you
    int[] dealerStock = { 30, 20, 60 };       // what a dealer has of each drug
    int restockMinutes = 7;                   // real minutes until an emptied drug is back
    int cokePackFrom = 7;                    // coke deals this size or bigger use the pack model
    // ---- robbing a dealer
    int dealerRobComply = 50;                 // % he gives up and drops his money (otherwise he pulls a gun)
    int dealerRobCashMin = 75, dealerRobCashMax = 380;
    int[] dealerRobDrugMin = { 3, 2, 4 }, dealerRobDrugMax = { 6, 6, 8 };
    string dealerFightWeapon = "Pistol";
    string[] drugModel = { "cscokebag", "csheroinbag", "cspillbottle" };
    string cokePackModel = "cj_coke_pack";
    // where each prop sits in the right hand: x, y, z offset, then x, y, z rotation (degrees)
    // 0 coke bag, 1 heroin, 2 pills, 3 coke pack
    // x, y, z offset from the right hand, x, y, z rotation in degrees. The bags, the pack and the cash lie
    // along the fingers (turned 90 degrees from how they used to sit across the hand)
    float[][] propPlace = {
        new float[] { 0.10f, 0.02f, -0.02f, 0f, 0f, 90f },
        new float[] { 0.10f, 0.02f, -0.02f, 270f, 0f, 195f },
        new float[] { 0.09f, 0.03f, -0.01f, 0f, 0f, 0f },
        new float[] { 0.12f, 0.03f, -0.04f, 75f, 345f, 195f },
        new float[] { 0.09f, 0.02f, -0.02f, 0f, 75f, 105f } };
    static readonly string[] PropKey = { "PropCoke", "PropHeroin", "PropPills", "PropCokePack", "PropMoney" };
    const int PROP_MONEY = 4;
    string moneyModel = "csmoneynote";
    float[] kindMult = { 0f, 2.0f, 1.5f, 1.15f, 1.7f, 1.4f };

    int punchChance = 15, robChance = 15, hookerStingChance = 30;   // % a buyer robs your drugs (punch) / your cash (frisk)
    int bulkPunchChance = 23, bulkRobChance = 22, bulkRobFrom = 10;  // same for a bulk sale: 10 g / 10 pills or more at once
    int punchCashMin = 26, punchCashMax = 132;
    int robCashMin = 30, robCashMax = 2000;
    int robMs = 6000, knockMs = 2000, punchHitMs = 350, punchDamage = 3;
    // the hand-over: wallet first, then buy_drugs / sell_drugs started on the same frame
    float dealDistance = 1.05f;               // how far apart the two stand (metres): hands meet, bodies don't
    int walletMs = 2200;                      // m_getoutwallet_pocket, then the trade
    // timed from the moment buy_drugs / sell_drugs start (measured in game):
    float handoverPhase = 0.45f;              // the hands meet at this point of take_obj (0-1): the drugs change hands
    int tradeHandoverMs = 1200;               // ...or this many ms in, if the game won't report the animation time
    int tradeEndMs = 3500;                    // the latest both stop; normally they stop when take_obj has played out
    int tradeStartAt;
    string robberWeapon = "Random";
    int cashRobberPistolChance = 75;            // the frisk robber (takes your cash): pistol, otherwise a knife
    int productRobberPistolChance = 50;         // the punch-and-run robber (takes the product): pistol, otherwise bare fists
    float friskDistance = 1.1f;
    bool friskFallback;               // the robber stands this far in front of you to go through your pockets
    int stingSquadCars = 5;                     // undercover hooker: squad cars sent after you

    bool dialogueCompat = true, ticketCompat = true;
    int searchChance = 30;                    // % of StopRightThere stops that turn into a search instead of a ticket
    int searchFindChance = 20;                // % the officer finds your drugs (only if you carry any)
    int searchMs = 5000;                      // the officer searches this long before the roll
    int cuffHoldMs = 5500;                    // crim_cuffed is held here (frame 165 at 30 fps): hands behind the back
    int lockpickPresses = 10, lockpickWindowMs = 700, lockpickAfterMs = 10000;
    float rideSpeed = 12f;                    // squad car speed to the police HQ, same units StopRightThere uses for its squad car
    float handsOnHeadPhase = 0.85f;           // armsup_2_searched_pose is frozen here: hands on the head
    float cuffDistance = 0.85f;               // he stops this far straight behind you (one footstep); cop_cuff steps him in to your hands
    int copNearDealRange = 15;                // a cop this close to a deal: 1 star, the NPC runs
    int fleeWantedLevel = 2;
    float arrestFleeDistance = 12f;

    int voiceVolume = 200;                    // player .wav volume, 0-1000, same scale as GreetTauntRob's VoiceVolume
    float npcVolume = 0.8f;
    bool showBlips = true, showMessages = true, sellOnlyUnarmed = true;
    string fontFamily = "";
    string fontFile = "";
    string dealerModel = "M_Y_DEALER";
    float uiRight = 70f, uiBottom = 150f;
    bool keepIniDealers = false;
    int dealerBlip = 82;                      // radar_multiplayer (drug meetup)

    int sellBeginChance = 100, sellAfterChance = 60;

    // the hand-over: whoever hands the drugs over plays give_obj, whoever gets them plays take_obj
    string[] animPlayerSell = { "amb@beg_standing", "give_obj" };     // you, selling to a buyer
    string[] animNpcBuy = { "amb@beg_standing", "take_obj" };         // the buyer
    string[] animPlayerBuy = { "amb@beg_standing", "take_obj" };      // you, buying from a dealer
    string[] animDealerSell = { "amb@beg_standing", "give_obj" };     // the dealer
    string[] animWallet = { "amb@atm", "m_getoutwallet_pocket" };
    string[] animPutAway = { "amb@atm", "m_putwalletaway_chest" };   // whoever ends up with the drugs puts them away
    string[] animPutAwayPocket = { "amb@atm", "m_putwalletaway_pocket" };   // whoever ends up with the cash puts it away
    string[] animWalletChest = { "amb@atm", "m_getoutwallet_chest" };      // selling: you take the drugs out of your jacket
    string[] animNpcSearch = { "amb@nightclub_ext", "bouncer_a_frisk" };
    string[] animNpcPunch = { "melee_unarmed_base", "partial_punch_r" };
    string[] animCopFrisk = { "cop", "cop_search" };
    string[] animPlayerFrisked = { "amb@nightclub_ext", "clubber_frisked" };
    string[] animTrunkSearch = { "cop", "copm_searchboot" };
    string[] animCloseBoot = { "car_boot", "close_boot" };
    string[] animPartnerLook = { "amb@bouncer_idles_b", "lookaround_a" };
    string[] animCheckUnder = { "cop_search_idles", "idle_check_under_vehicle" };
    string[] animCopCarChat = { "amb@bridgecops", "car_chat_outside" };
    string[] animArmsUp = { "cop", "armsup_loop" };
    string[] animArmsToSearched = { "cop", "armsup_2_searched_pose" };
    string[] animSearchedPose = { "cop", "searched_pose" };
    string[] animCrimCuffed = { "cop", "crim_cuffed" };
    string[] animGetInCuffed = { "veh@std", "get_in_ds" };          // you, into the back seat, hands behind your back
    string[] animCopCloseDoor = { "veh@std", "d_close_out_ds" };    // the officer shuts the rear door from outside
    string[] animCopOpenDoor = { "veh@std", "d_open_out_ds" };      // the officer opens the rear door from outside
    string[] animDealerIdle = { "amb@default", "bouncer_default" };          // arms crossed
    string[] animDealerTalk = { "amb@nightclub_ext", "street_chat_a" };
    string[] animDealerDropCash = { "amb@beg_standing", "give_obj" };

    // NPC lines: speech contexts the game plays in the NPC's own voice (his model's voice bank).
    // One is picked at random; if his voice has no such line the next one is tried.
    string[] speechDealerGreet = { "GENERIC_HI", "CHAT_RESP" };              // he walks up to you
    string[] speechDealerDone = { "THANKS", "GENERIC_BYE", "GENERIC_YES" };  // after the hand-over
    string[] speechDealerNoSale = { "GENERIC_DEJECTED", "GENERIC_BYE" };     // you backed off without buying
    string[] speechDealerRobbed = { "SHOCKED", "GENERIC_NO", "GENERIC_DEJECTED" };   // hands up at gunpoint
    string[] speechDealerFight = { "FIGHT", "GENERIC_FUCK_OFF", "INTIMIDATE_RESP" }; // he pulls a gun
    string[] speechBuyerAgree = { "GENERIC_YES", "CHAT_RESP", "GENERIC_HI" };       // he walks up to buy
    string[] speechBuyerDone = { "THANKS", "GENERIC_BYE" };                  // after he has bought
    string[] speechBuyerNoSale = { "GENERIC_NO", "GENERIC_DEJECTED", "GENERIC_BYE" }; // you backed off without selling
    string[] speechHostile = { "FIGHT", "GENERIC_FUCK_OFF", "INTIMIDATE_RESP" };    // a buyer punches you
    string[] speechRob = { "INTIMIDATE_RESP", "GENERIC_FUCK_OFF", "JEERING" };      // a buyer robs you
    string[] speechHookerSting = { "SOLICIT_TURNED_DOWN_01", "SOLICIT_TURNED_DOWN_02", "SOLICIT_TURNED_DOWN_03" };   // undercover hooker: one at random
    // cop voice banks (used when a cop's own voice has no line for the moment)
    string[] copVoices = { "M_M_FATCOP_01_BLACK", "M_M_FATCOP_01_WHITE", "M_Y_COP_BLACK", "M_Y_COP_BLACK_02",
        "M_Y_COP_HISPANIC", "M_Y_COP_TRAFFIC_BLACK", "M_Y_COP_TRAFFIC_HISPANIC", "M_Y_COP_TRAFFIC_WHITE",
        "M_Y_COP_WHITE", "M_Y_COP_WHITE_02" };
    string[] animPlayerOpenTrunk = { "amb@car_stash", "open_boot" };
    string[] animCopRadio = { "cop_search_idles", "idle_answer_radio_a" };
    string[] animCopCuff = { "cop", "cop_cuff" };
    int arrestFine = 250;
    int buyCount;

    string[] businessModels = {
        "M_Y_BUSINESS_01", "M_Y_BUSINESS_02", "M_M_BUSINESS_02", "M_M_BUSINESS_03",
        "M_M_PBUSINESS_01", "M_Y_PRICH_01", "M_M_PRICH_01", "M_Y_PMANHAT_01", "M_Y_PMANHAT_02",
        "M_M_PMANHAT_01", "M_M_PMANHAT_02", "M_M_LAWYER_01", "M_M_LAWYER_02" };
    string[] hookerModels = { "F_Y_HOOKER_01", "F_Y_HOOKER_03", "F_Y_STREET_02", "F_Y_STREET_05",
        "F_Y_STREET_09", "F_Y_STREET_12", "F_Y_STREET_30", "F_Y_STREET_34" };
    string[] bumModels = { "M_M_GENBUM_01", "M_M_TRAMPBLACK", "M_O_STREET_01", "F_O_BINCO" };
    string[] gangModels = {
        "M_Y_GAFR_LO_01", "M_Y_GAFR_LO_02", "M_Y_GAFR_HI_01", "M_Y_GAFR_HI_02",
        "M_Y_GALB_LO_01", "M_Y_GALB_LO_02", "M_Y_GALB_LO_03", "M_Y_GALB_LO_04",
        "M_M_GBIK_LO_03", "M_Y_GBIK_HI_01", "M_Y_GBIK_HI_02", "M_Y_GBIK02_LO_02", "M_Y_GBIK_LO_01", "M_Y_GBIK_LO_02",
        "M_Y_GIRI_LO_01", "M_Y_GIRI_LO_02", "M_Y_GIRI_LO_03",
        "M_M_GJAM_HI_01", "M_M_GJAM_HI_02", "M_M_GJAM_HI_03", "M_Y_GJAM_LO_01", "M_Y_GJAM_LO_02",
        "M_Y_GKOR_LO_01", "M_Y_GKOR_LO_02",
        "M_Y_GLAT_HI_01", "M_Y_GLAT_HI_02", "M_Y_GLAT_LO_01", "M_Y_GLAT_LO_02",
        "M_Y_GMAF_HI_01", "M_Y_GMAF_HI_02", "M_Y_GMAF_LO_01", "M_Y_GMAF_LO_02",
        "M_O_GRUS_HI_01", "M_Y_GRUS_LO_01", "M_Y_GRUS_LO_02", "M_Y_GRUS_HI_02",
        "M_M_GRU2_HI_01", "M_M_GRU2_HI_02", "M_M_GRU2_LO_02", "M_Y_GRU2_LO_01",
        "M_M_GTRI_HI_01", "M_M_GTRI_HI_02", "M_Y_GTRI_LO_01", "M_Y_GTRI_LO_02" };

    // ------------------------------------------------------------------ state
    readonly Random rnd = new Random();
    readonly Dictionary<string, string> ini = new Dictionary<string, string>();
    readonly List<string> iniDealers = new List<string>();
    readonly List<Dealer> dealers = new List<Dealer>();
    readonly Dictionary<int, Offer> offers = new Dictionary<int, Offer>();
    readonly List<Thief> thieves = new List<Thief>();
    readonly List<Loot> loots = new List<Loot>();
    readonly List<Held> held = new List<Held>();
    readonly List<Squad> squads = new List<Squad>();
    readonly List<Voice> voices = new List<Voice>();
    readonly List<Speech> speeches = new List<Speech>();
    readonly List<Delayed> delayed = new List<Delayed>();
    readonly Dictionary<string, AnimationSet> animSets = new Dictionary<string, AnimationSet>();
    readonly Dictionary<string, bool> badSets = new Dictionary<string, bool>();
    readonly Dictionary<string, string> lastPick = new Dictionary<string, string>();
    readonly Btn[] btn = { new Btn(), new Btn(), new Btn(), new Btn(), new Btn(), new Btn() };
    readonly Texture[] drugIcon = new Texture[3];
    Texture packIcon;

    // ------------------------------------------------------------------ pickup icons above the minimap
    // 1 icon  = right above the top of the minimap (the refuel icon spot)
    // 2 icons = one either side of that spot, on the minimap's curve
    // 3 icons = both sides and the middle
    static readonly string[] PickupIconFile = { "coke_pack_pickup.png", "heroin_pickup.png", "pills_pickup.png" };
    readonly Texture[] pickupIcon = new Texture[3];
    const int PickupFadeLevels = 20;                   // the game ignores a sprite's alpha, so each icon gets 20 pre-faded copies (same as ReFuel)
    readonly Texture[][] pickupFade = new Texture[3][];
    readonly bool[] pickupShow = new bool[3];
    int pickupStart;                                   // Environment.TickCount when they came up
    bool pickupOn;                                     // icons are up (the Windows timer can be negative, so -1 can't mean 'none')
    float pickupX = 0.107f, pickupY = 0.720f;          // the middle (single icon) spot, 0-1 of the screen = ReFuel's fuel icon spot
    float pickupSize = 0.040f, pickupArc = 0.12f;      // icon height / distance from the minimap's centre, 0-1 of screen height
    float pickupSpread = 30f;                          // degrees between icons along the curve
    int pickupFadeIn = 1000, pickupHold = 3000, pickupFadeOut = 1000;   // fade in 1 s, stay 3 s, fade out 1 s
    bool pendingGiveMax;
    int shipmentSeen = -1;                             // last "Pusher.ShipmentStolen" count handled (-1 = not read yet)
    readonly Dictionary<string, int> rep = new Dictionary<string, int>();
    GTA.Object sceneProp, sceneMoney;
    bool putMeDone, putNpcDone;
    bool sceneDoor;
    Dictionary<int, int> modelKinds;
    int[] stash = new int[3];
    string baseDir, episodeName = "IV";
    bool inited;
    int logCount;

    // menus
    int mode;
    Dealer curDealer;
    Ped curBuyer;
    Offer curOffer;
    int menuOpened, ltReleasedAt, buyLockUntil, lastTargetScan;
    readonly int[] order = new int[3];       // bought at this dealer since his menu opened
    int lastBuyPress, lastBoughtDrug = -1;
    // a dealer / buyer walking up
    Dealer approachDl;
    string approachTitle;
    Vector3 approachTarget;
    int approachIssued;
    // prop test key
    Keys keyPropTest = Keys.None;
    int pendingTurn = -1, pendingTurnSign = 1, pendingTune = -1;
    Keys keyTuneMoney = Keys.F4, keyTuneCokePack = Keys.F5, keyTuneHeroin = Keys.F6;
    GTA.Object tuneObj;
    int tuneKind = -1;
    const float TuneStep = 15f;            // degrees per press of 7 / 8 / 9 (Shift: the other way)
    int propTestIdx;
    GTA.Object propTest;
    int propTestUntil;
    int hudMoney;             // read in the tick so the draw call never touches the game
    int lastSellPress;
    bool hudInVehicle;
    bool ltWasHeld;
    Ped scanTarget;

    // scenes
    int scene, scenePhase, sceneStart, phaseStart, lastAnim;
    bool sceneFlag, scenePaid, punchFromRob, robDeducted;
    Ped scenePed;
    Offer sceneOffer;
    int sceneDrug, sceneQty, sceneCash;

    // police search (ticket script)
    Ped searchCop, pendingSearchCop;
    Vehicle searchVeh, pendingSearchVeh;
    bool pendingSearch, searchBike;
    int searchState;

    // arrest mode
    bool arrestActive;
    Vector3 arrestPos;
    int arrestSince;

    // requests coming from other scripts
    Ped pendingSell, pendingOpenDealer, pendingRobStart;
    bool pendingPropTest, pendingRobFight;
    readonly List<Ped> pendingDrops = new List<Ped>();
    class RobbedDealer { public Ped Ped; public int Until; }
    readonly List<RobbedDealer> robbedDealers = new List<RobbedDealer>();
    bool dealerLockOn, buyFromLockOn;
    string pendingSellTitle, sellTitle = "BUYER";

    // input
    ushort padButtons;
    bool ltHeld, rtHeld, stickMoved, aimHeld, padOk = true;
    int lastCleanup;

    // audio
    bool waveOk = true;
    SoundPlayer snd;
    System.IO.MemoryStream sndStream;

    // ------------------------------------------------------------------ native imports
    [StructLayout(LayoutKind.Sequential)]
    struct XGamepad { public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [StructLayout(LayoutKind.Sequential)]
    struct XState { public uint Packet; public XGamepad Pad; }
    [DllImport("xinput9_1_0.dll")]
    static extern int XInputGetState(int index, out XState state);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern int AddFontResourceEx(string file, uint flags, IntPtr reserved);

    [StructLayout(LayoutKind.Sequential)]
    struct WaveFormat { public ushort Tag, Channels; public uint Rate, AvgBytes; public ushort BlockAlign, Bits, Extra; }
    [DllImport("winmm.dll")]
    static extern int waveOutOpen(out IntPtr handle, int device, ref WaveFormat format, IntPtr callback, IntPtr instance, int flags);
    [DllImport("winmm.dll")]
    static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")]
    static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")]
    static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);
    [DllImport("winmm.dll")]
    static extern int waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")]
    static extern int waveOutClose(IntPtr handle);

    const ushort PAD_A = 0x1000, PAD_B = 0x2000, PAD_X = 0x4000, PAD_Y = 0x8000;

    // ------------------------------------------------------------------ startup
    public Pusher()
    {
        baseDir = Path.Combine(Path.Combine(Game.InstallFolder, "scripts"), "Pusher");

        btn[B_SELL].Name = "RT"; btn[B_SELL].Key = Keys.NumPad0;
        btn[B_D0 + COKE].Name = "RT"; btn[B_D0 + COKE].Key = Keys.NumPad1;
        btn[B_D0 + HEROIN].Name = "DpadLeft"; btn[B_D0 + HEROIN].Key = Keys.NumPad2;
        btn[B_D0 + PILLS].Name = "DpadRight"; btn[B_D0 + PILLS].Key = Keys.NumPad3;
        btn[B_ROB].Name = "LB"; btn[B_ROB].Key = Keys.NumPad4;
        btn[B_COMPLY].Name = "RB"; btn[B_COMPLY].Key = Keys.NumPad5;

        AppDomain d = AppDomain.CurrentDomain;
        d.SetData("Pusher.Version", VERSION);
        d.SetData("Pusher.IsBuyer", new Predicate<Ped>(IsBuyer));
        d.SetData("Pusher.IsDealer", new Predicate<Ped>(IsDealer));
        d.SetData("Pusher.OpenSell", new Action<Ped>(OpenSell));
        d.SetData("Pusher.OpenSellAs", new Action<Ped, string>(OpenSellAs));
        d.SetData("Pusher.BeginSearch", new Func<Ped, Vehicle, bool>(BeginSearch));
        d.SetData("Pusher.BeginSearch2", new Func<Ped, Ped, Vehicle, Vehicle, bool>(BeginSearch2));
        d.SetData("Pusher.OpenDealer", new Action<Ped>(OpenDealerRequest));
        d.SetData("Pusher.RobDealerStart", new Action<Ped>(RobDealerStartRequest));
        d.SetData("Pusher.RobDealer", new Func<Ped, bool>(RobDealerRequest));
        d.SetData("Pusher.DealerDrop", new Action<Ped>(DealerDropRequest));
        d.SetData("Pusher.GiveMaxDrugs", new Func<bool>(GiveMaxDrugsRequest));
        d.SetData("Pusher.SearchState", 0);
        d.SetData("Pusher.Busy", false);
        d.SetData("Pusher.Carrying", false);

        Interval = 0;          // every frame: the menus are drawn the same way as GreetTauntRob's
        // anything a search left switched on if scripts were reloaded mid-search is switched back off
        try
        {
            GTA.Native.Function.Call("SET_POLICE_IGNORE_PLAYER", GTA.Native.Function.Call<int>("GET_PLAYER_ID"), false);
            GTA.Native.Function.Call("SET_CHAR_COLLISION", Player.Character, true);
            GTA.Native.Function.Call("FREEZE_CHAR_POSITION", Player.Character, false);
            Player.CanControlCharacter = true;
        }
        catch { }
        Tick += new EventHandler(OnTick);
        KeyDown += new GTA.KeyEventHandler(OnKeyDown);
        PerFrameDrawing += new GraphicsEventHandler(OnDraw);
    }

    void Init()
    {
        inited = true;
        try
        {
            if (!Directory.Exists(baseDir)) Directory.CreateDirectory(baseDir);
            switch (Game.CurrentEpisode)
            {
                case GameEpisode.TLAD: episodeName = "TLAD"; break;
                case GameEpisode.TBOGT: episodeName = "TBoGT"; break;
                default: episodeName = "IV"; break;
            }
            LoadIni();
            LoadLocations();
            LoadHq();
            LoadStash();
            LoadFont();
            LoadIcons();
            LoadUi();
        }
        catch (Exception ex) { Log("Init: " + ex); }

        if (dealers.Count == 0) AddDefaultDealers();
        if (showBlips)
        {
            // blips are added / removed by distance in UpdateDealers
        }
        Publish();
    }

    void Log(string text)
    {
        if (logCount > 300) return;
        logCount++;
        try
        {
            File.AppendAllText(Path.Combine(baseDir, "Pusher.log"),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine);
        }
        catch { }
    }

    void Note(string text)
    {
        if (!showMessages) return;
        try { Game.DisplayText(text, 3500); } catch { }
    }

    void MakeBlip(Dealer dl)
    {
        try
        {
            dl.Blip = Blip.AddBlip(dl.Pos);
            try { dl.Blip.Icon = (BlipIcon)dealerBlip; } catch { }
            dl.Blip.Scale = 0.8f;
            dl.Blip.ShowOnlyWhenNear = false;      // on the minimap and the pause map, but only while you are within BlipRange
            dl.Blip.Name = "Dealer";
        }
        catch { }
    }

    float blipRange = 150f;

    // the blip exists only while you are within BlipRange metres of the dealer
    void UpdateBlip(Dealer dl, float d2)
    {
        bool want = showBlips && d2 <= blipRange * blipRange;
        if (want && dl.Blip == null) MakeBlip(dl);
        else if (!want && dl.Blip != null)
        {
            try { dl.Blip.Delete(); } catch { }
            dl.Blip = null;
        }
    }

    void AddDefaultDealers()
    {
        // Approximate spots - each dealer is snapped to the nearest pavement when he spawns.
        // Move / add your own in Pusher.ini, or stand somewhere and press F10.
        AddDealer("Hove Beach", 880f, -480f, 15f, 1.00f, 1.00f, 1.10f);
        AddDealer("South Bohan", 600f, 1400f, 12f, 0.80f, 1.10f, 1.20f);
        AddDealer("Northwood", -350f, 1500f, 15f, 0.90f, 0.85f, 1.15f);
        AddDealer("Middle Park", -100f, 900f, 10f, 1.20f, 1.10f, 0.90f);
        AddDealer("Star Junction", -230f, 430f, 15f, 1.30f, 1.20f, 0.85f);
        AddDealer("Chinatown", 10f, -350f, 15f, 1.10f, 0.90f, 1.00f);
        AddDealer("Steinway", 1150f, 800f, 30f, 0.95f, 1.00f, 1.05f);
        AddDealer("Alderney City", -1100f, 600f, 15f, 1.05f, 0.80f, 1.10f);
        AddDealer("Acter", -1300f, -300f, 5f, 0.85f, 0.75f, 1.25f);
        AddDealer("Westdyke", -1200f, 1600f, 25f, 1.15f, 1.25f, 0.95f);
    }

    Dealer AddDealer(string name, float x, float y, float z, float w, float m, float c)
    {
        Dealer dl = new Dealer();
        dl.Name = name;
        dl.Pos = new Vector3(x, y, z);
        dl.Mult[COKE] = w; dl.Mult[HEROIN] = m; dl.Mult[PILLS] = c;
        dealers.Add(dl);
        return dl;
    }

    // ------------------------------------------------------------------ ini
    static float F(string s, float def)
    {
        float v;
        return float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : def;
    }
    static int I(string s, int def)
    {
        int v;
        return int.TryParse(s.Trim(), out v) ? v : def;
    }
    static string[] L(string s)
    {
        List<string> r = new List<string>();
        foreach (string p in s.Split(',')) if (p.Trim().Length > 0) r.Add(p.Trim());
        return r.ToArray();
    }
    static string S(float f) { return f.ToString("0.##", CultureInfo.InvariantCulture); }
    static string P4(float f) { return f.ToString("0.####", CultureInfo.InvariantCulture); }
    static string J(string[] a) { return string.Join(", ", a); }

    int GI(string key, int def) { string v; return ini.TryGetValue(key.ToLowerInvariant(), out v) ? I(v, def) : def; }
    float GF(string key, float def) { string v; return ini.TryGetValue(key.ToLowerInvariant(), out v) ? F(v, def) : def; }
    bool GB(string key, bool def) { string v; return ini.TryGetValue(key.ToLowerInvariant(), out v) ? I(v, def ? 1 : 0) != 0 : def; }
    string GS(string key, string def) { string v; return ini.TryGetValue(key.ToLowerInvariant(), out v) ? v : def; }
    string[] GL(string key, string[] def) { string v; return ini.TryGetValue(key.ToLowerInvariant(), out v) ? L(v) : def; }
    string[] GA(string key, string[] def)
    {
        string v;
        if (!ini.TryGetValue(key.ToLowerInvariant(), out v)) return def;
        string[] p = L(v);
        return p.Length >= 2 ? new string[] { p[0], p[1] } : new string[] { "", "" };
    }
    Keys GK(string key, Keys def)
    {
        string v;
        if (!ini.TryGetValue(key.ToLowerInvariant(), out v)) return def;
        try { return (Keys)Enum.Parse(typeof(Keys), v.Trim(), true); } catch { return def; }
    }

    void LoadIni()
    {
        string path = Path.Combine(baseDir, "Pusher.ini");
        bool exists = File.Exists(path);
        if (exists)
        {
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') continue;
                int eq = line.IndexOf('=');
                if (eq < 1) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();
                if (key == "dealer") iniDealers.Add(val); else ini[key] = val;
            }
        }

        // an ini from Pusher 1/2 still has weed / meth: drop its drug and button lines
        if (exists && GI("IniVersion", 0) < 3)
        {
            List<string> old = new List<string>();
            foreach (string k in ini.Keys)
                if (k.StartsWith("max") || k.StartsWith("price") || k.StartsWith("buystep") || k.StartsWith("sellm")
                    || k.StartsWith("button") || (k.StartsWith("key") && k != "keyadddealer")) old.Add(k);
            foreach (string k in old) ini.Remove(k);
        }
        if (exists && GI("IniVersion", 0) < 6) ini.Remove("keyadddealer");      // the save key is F5 now
        if (exists && GI("IniVersion", 0) < 56)                                 // 3.55: the lined-up props are the defaults, no line-up keys
        {
            foreach (string k in new string[] { "keylineupmoney", "keylineupcokepack", "keylineupheroin", "keyproptest" }) ini.Remove(k);
            if (GS("PropHeroin", "").Replace(" ", "") == "0.1,0.02,-0.02,0,0,90") ini.Remove("propheroin");
            if (GS("PropCokePack", "").Replace(" ", "") == "0.12,0.03,-0.04,0,0,90") ini.Remove("propcokepack");
            if (GS("PropMoney", "").Replace(" ", "") == "0.09,0.02,-0.02,0,0,90") ini.Remove("propmoney");
        }
        if (exists && GI("IniVersion", 0) < 55) { ini.Remove("modelmoney"); ini.Remove("keyproptest"); }   // 3.54: csmoneynote, F4 / F5 / F6 line-up keys
        if (exists && GI("IniVersion", 0) < 54)                                 // 3.53: props lie along the fingers, cash prop
            foreach (string k in new string[] { "propcoke", "propheroin", "propcokepack", "propmoney" }) ini.Remove(k);
        if (exists && GI("IniVersion", 0) < 47)                                 // 3.46: undercover hooker 30%, her line, she runs, 5 squad cars
            foreach (string k in new string[] { "hookerstingchance", "stingsquadcars", "speechhookersting" }) ini.Remove(k);
        if (exists && GI("IniVersion", 0) < 46)                                 // 3.45: cash 15 / drugs 15, bulk cash 22 / drugs 23
            foreach (string k in new string[] { "punchchance", "robchance", "bulkpunchchance", "bulkrobchance", "bulkrobfrom" }) ini.Remove(k);
        if (exists && GI("IniVersion", 0) < 34) ini.Remove("cuffdistance");    // 3.33: one footstep, not locked
        if (exists && GI("IniVersion", 0) < 32) { ini.Remove("ridespeedmph"); ini.Remove("cuffdistance"); }   // 3.31
        if (exists && GI("IniVersion", 0) < 27) ini.Remove("cuffdistance");    // 3.26: measured hip to hip now
        if (exists && GI("IniVersion", 0) < 26) ini.Remove("robberweapon");    // 3.25: knife or pistol at random
        if (exists && GI("IniVersion", 0) < 24)                                 // 3.23: search pacing / anims fixed
            foreach (string k in new string[] { "animcloseboot", "ridespeedmph", "animcheckunder" }) ini.Remove(k);
        if (exists && GI("IniVersion", 0) < 23) ini.Remove("buttoncoke");      // Coke is on RT now
        if (exists && GI("IniVersion", 0) < 22) ini.Remove("animarmstosearched");   // armsup_2_searched_pose
        if (exists && GI("IniVersion", 0) < 21)                                 // 3.19: the new StopRightThere search
        {
            foreach (string k in new string[] { "trunksearchchance", "patdownchance", "trunkfindchance", "patdownfindchance",
                "searchms", "animcopfrisk", "animtrunksearch", "animcopcuff", "animplayeropentrunk" }) ini.Remove(k);
        }
        if (exists && GI("IniVersion", 0) < 20) { ini.Remove("buttonselldrugs"); ini.Remove("buttoncoke"); }   // 3.18: RT / D-pad down, RB is cover
        if (exists && GI("IniVersion", 0) < 19)                                 // 3.17: give_obj / take_obj hand-over
        {
            foreach (string k in new string[] { "animplayersell", "animnpcbuy", "animplayerbuy", "animdealersell",
                "dealdistance", "handoverphase", "tradehandoverms", "tradeendms" }) ini.Remove(k);
        }
        if (exists && GI("IniVersion", 0) < 18)                                 // 3.16: no curb buyers, no F5 key
        {
            List<string> old = new List<string>();
            foreach (string k in ini.Keys) if (k.StartsWith("carbuyer") || k.StartsWith("carrob") || k == "sellcarchance" || k == "keyadddealer") old.Add(k);
            foreach (string k in old) ini.Remove(k);
        }
        if (exists && GI("IniVersion", 0) < 17) { ini.Remove("playervolume"); ini.Remove("volume"); ini.Remove("voicevolume"); }   // 3.15: GreetTauntRob volume scale
        if (exists && GI("IniVersion", 0) < 16)                                 // 3.14: new trade timing and distance
        {
            ini.Remove("dealdistance"); ini.Remove("handoverphase"); ini.Remove("dealendphase");
            ini.Remove("handoverms"); ini.Remove("dealmaxms");
        }
        if (exists && GI("IniVersion", 0) < 15) ini.Remove("cokepackfrom");      // the pack is used from 7g now
        if (exists && GI("IniVersion", 0) < 14 && GS("ModelCokePack", "").Trim().ToLowerInvariant() == "p_coke_pack")
            ini.Remove("modelcokepack");                                        // the big coke prop is cj_coke_pack
        if (exists && GI("IniVersion", 0) < 13)                                 // 3.10: NPC lines reworked (game voices only)
        {
            List<string> old = new List<string>();
            foreach (string k in ini.Keys) if (k.StartsWith("speech") || k == "npcvolume") old.Add(k);
            foreach (string k in old) ini.Remove(k);
        }
        if (exists && GI("IniVersion", 0) < 12)                                 // 3.9: bulk = 5% off, dealers fight back half the time
        {
            List<string> old = new List<string>();
            foreach (string k in ini.Keys) if (k.StartsWith("bulksell") || k == "dealerrobcomplychance") old.Add(k);
            foreach (string k in old) ini.Remove(k);
        }
        if (exists && GI("IniVersion", 0) < 11) { ini.Remove("sellbeginchance"); ini.Remove("sellendchance"); }   // 3.8: SellBegin always, SellEnd became SellAfter
        if (exists && GI("IniVersion", 0) < 10)                                 // 3.7: pack prices, new sell prices, wants 1-9
        {
            List<string> old = new List<string>();
            foreach (string k in ini.Keys)
                if (k.StartsWith("buycoke") || k.StartsWith("buyheroin") || k.StartsWith("buypills")
                    || k.StartsWith("regularcoke") || k.StartsWith("regularheroin") || k.StartsWith("regularpills")
                    || k == "sellcoke" || k == "sellheroin" || k == "sellpills"
                    || k.StartsWith("sellmin") || k.StartsWith("sellmax") || k == "prophandoffset") old.Add(k);
            foreach (string k in old) ini.Remove(k);
        }
        if (exists && GI("IniVersion", 0) < 8)                                  // the price system changed in 3.4
        {
            List<string> old = new List<string>();
            foreach (string k in ini.Keys)
                if (k.StartsWith("price") || k.StartsWith("buystep") || k.StartsWith("bulkstep") || k.StartsWith("bigorder")
                    || k.StartsWith("payout") || k.StartsWith("relationship") || k.StartsWith("bulkdiscount")
                    || k == "buttonbulk" || k == "keybulk") old.Add(k);
            foreach (string k in old) ini.Remove(k);
        }
        keepIniDealers = GB("KeepIniDealers", keepIniDealers);
        dealerBlip = GI("BlipDealer", dealerBlip);
        sellBeginChance = GI("SellBeginChance", sellBeginChance);
        sellAfterChance = GI("SellAfterChance", sellAfterChance);
        pickupX = GF("PickupIconX", pickupX);
        pickupY = GF("PickupIconY", pickupY);
        pickupSize = GF("PickupIconSize", pickupSize);
        // the first version of these defaults sat off the refuel icon's spot: move an untouched ini over to it
        if (Math.Abs(pickupX - 0.085f) < 0.0001f && Math.Abs(pickupY - 0.75f) < 0.0001f && Math.Abs(pickupSize - 0.042f) < 0.0001f)
        { pickupX = 0.107f; pickupY = 0.720f; pickupSize = 0.040f; }
        pickupArc = GF("PickupIconArc", pickupArc);
        pickupSpread = GF("PickupIconSpread", pickupSpread);
        pickupFadeIn = GI("PickupIconFadeInMs", pickupFadeIn);
        pickupHold = GI("PickupIconHoldMs", pickupHold);
        if (pickupHold == 2000) pickupHold = 3000;   // the first default: icons now stay 3 s
        pickupFadeOut = GI("PickupIconFadeOutMs", pickupFadeOut);
        for (int i = 0; i < 3; i++)
        {
            string n = DrugName[i];
            maxCarry[i] = Math.Max(1, GI("Max" + n, maxCarry[i]));
            buyPrice[i] = Math.Max(1, GI("BuyPack" + n, buyPrice[i]));
            regularPrice[i] = Math.Max(1, GI("RegularPack" + n, regularPrice[i]));
            dealerStock[i] = Math.Max(1, GI("DealerStock" + n, dealerStock[i]));
            sellPrice[i] = Math.Max(1, GI("Sell" + n, sellPrice[i]));
            buyStep[i] = Math.Max(1, GI("BuyPerPress" + n, buyStep[i]));
            sellMin[i] = Math.Max(1, GI("SellMin" + n, sellMin[i]));
            sellMax[i] = Math.Max(sellMin[i], GI("SellMax" + n, sellMax[i]));
            dealerRobDrugMin[i] = Math.Max(1, GI("DealerRobMin" + n, dealerRobDrugMin[i]));
            dealerRobDrugMax[i] = Math.Max(dealerRobDrugMin[i], GI("DealerRobMax" + n, dealerRobDrugMax[i]));
        }
        visitsForDiscount = Math.Max(1, GI("VisitsForRegularPrice", visitsForDiscount));
        bulkBuyerChance = GI("BulkBuyerChance", bulkBuyerChance);
        bulkAmount = Math.Max(1, GI("BulkAmount", bulkAmount));
        bulkDiscount = Math.Max(0, Math.Min(90, GI("BulkDiscount", bulkDiscount)));
        for (int i = 0; i < 3; i++)
            bulkSellPrice[i] = Math.Max(1, (int)Math.Round(bulkAmount * sellPrice[i] * (100 - bulkDiscount) / 100.0, MidpointRounding.AwayFromZero));
        bigOrderFrom = Math.Max(1, GI("BigOrderFrom", bigOrderFrom));
        bigOrderRobChance = Math.Max(0, Math.Min(100, GI("BigOrderRobChance", bigOrderRobChance)));
        restockMinutes = Math.Max(1, GI("RestockMinutes", restockMinutes));
        dealDistance = Math.Max(0.5f, Math.Min(2.0f, GF("DealDistance", dealDistance)));
        walletMs = Math.Max(0, GI("WalletMs", walletMs));
        handoverPhase = Math.Max(0.05f, Math.Min(0.95f, GF("HandoverPhase", handoverPhase)));
        tradeHandoverMs = Math.Max(200, GI("TradeHandoverMs", tradeHandoverMs));
        tradeEndMs = Math.Max(tradeHandoverMs + 200, GI("TradeEndMs", tradeEndMs));
        cokePackFrom = Math.Max(1, GI("CokePackFrom", cokePackFrom));
        dealerRobComply = GI("DealerRobComplyChance", dealerRobComply);
        dealerRobCashMin = GI("DealerRobCashMin", dealerRobCashMin);
        dealerRobCashMax = Math.Max(dealerRobCashMin, GI("DealerRobCashMax", dealerRobCashMax));
        dealerFightWeapon = GS("DealerFightWeapon", dealerFightWeapon);
        drugModel[COKE] = GS("ModelCokeBag", drugModel[COKE]);
        cokePackModel = GS("ModelCokePack", cokePackModel);
        drugModel[HEROIN] = GS("ModelHeroin", drugModel[HEROIN]);
        drugModel[PILLS] = GS("ModelPills", drugModel[PILLS]);
        moneyModel = GS("ModelMoney", moneyModel);
        LoadPropPlaces();

        punchChance = GI("PunchChance", punchChance);
        robChance = GI("RobChance", robChance);
        bulkPunchChance = GI("BulkPunchChance", bulkPunchChance);
        bulkRobChance = GI("BulkRobChance", bulkRobChance);
        bulkRobFrom = Math.Max(1, GI("BulkRobFrom", bulkRobFrom));
        hookerStingChance = GI("HookerStingChance", hookerStingChance);
        punchCashMin = GI("PunchCashMin", punchCashMin);
        punchCashMax = Math.Max(punchCashMin, GI("PunchCashMax", punchCashMax));
        robCashMin = GI("RobCashMin", robCashMin);
        robCashMax = Math.Max(robCashMin, GI("RobCashMax", robCashMax));
        robMs = Math.Max(2000, GI("RobSearchMs", robMs));
        knockMs = Math.Max(500, GI("KnockdownMs", knockMs));
        punchDamage = Math.Max(0, GI("PunchDamage", punchDamage));
        robberWeapon = GS("RobberWeapon", robberWeapon);
        cashRobberPistolChance = Math.Max(0, Math.Min(100, GI("CashRobberPistolChance", cashRobberPistolChance)));
        productRobberPistolChance = Math.Max(0, Math.Min(100, GI("ProductRobberPistolChance", productRobberPistolChance)));
        friskDistance = Math.Max(0.5f, Math.Min(2f, GF("FriskDistance", friskDistance)));
        stingSquadCars = Math.Max(0, Math.Min(8, GI("StingSquadCars", stingSquadCars)));

        dialogueCompat = GB("GreetTauntRobCompat", dialogueCompat);
        ticketCompat = GB("StopRightThereCompat", ticketCompat);
        searchChance = Math.Max(0, Math.Min(100, GI("SearchChance", searchChance)));
        searchFindChance = Math.Max(0, Math.Min(100, GI("SearchFindChance", searchFindChance)));
        searchMs = Math.Max(1500, GI("SearchMs", searchMs));
        cuffHoldMs = Math.Max(500, GI("CuffHoldMs", cuffHoldMs));
        lockpickPresses = Math.Max(1, GI("LockpickPresses", lockpickPresses));
        lockpickWindowMs = Math.Max(200, GI("LockpickWindowMs", lockpickWindowMs));
        lockpickAfterMs = Math.Max(0, GI("LockpickAfterMs", lockpickAfterMs));
        rideSpeed = Math.Max(4f, Math.Min(40f, GF("RideSpeed", rideSpeed)));
        handsOnHeadPhase = Math.Max(0.3f, Math.Min(1f, GF("HandsOnHeadPhase", handsOnHeadPhase)));
        cuffDistance = Math.Max(0.2f, Math.Min(1.5f, GF("CuffDistance", cuffDistance)));
        cuffHipDistance = Math.Max(0.2f, Math.Min(1.2f, GF("CuffHipDistance", cuffHipDistance)));
        haltBehindCop = Math.Max(0f, Math.Min(40f, GF("HaltBehindSquadCar", haltBehindCop)));
        haltAheadOfYou = Math.Max(0f, Math.Min(40f, GF("HaltAheadOfYourCar", haltAheadOfYou)));
        haltHalfWidth = Math.Max(0f, Math.Min(40f, GF("HaltRoadHalfWidth", haltHalfWidth)));
        handsUpToLoopMs = Math.Max(300, Math.Min(5000, GI("HandsUpToLoopMs", handsUpToLoopMs)));
        getInHeadingOffset = GF("GetInHeadingOffset", getInHeadingOffset);
        animPartnerLook = GA("AnimPartnerLook", animPartnerLook);
        copNearDealRange = Math.Max(0, GI("CopNearDealRange", copNearDealRange));
        animCloseBoot = GA("AnimCloseBoot", animCloseBoot);
        animCheckUnder = GA("AnimCheckUnder", animCheckUnder);
        animCopCarChat = GA("AnimCopCarChat", animCopCarChat);
        animArmsUp = GA("AnimArmsUp", animArmsUp);
        animArmsToSearched = GA("AnimArmsToSearched", animArmsToSearched);
        animSearchedPose = GA("AnimSearchedPose", animSearchedPose);
        animCrimCuffed = GA("AnimCrimCuffed", animCrimCuffed);
        animGetInCuffed = GA("AnimGetInCuffed", animGetInCuffed);
        animCopCloseDoor = GA("AnimCopCloseDoor", animCopCloseDoor);
        animCopOpenDoor = GA("AnimCopOpenDoor", animCopOpenDoor);
        fleeWantedLevel = Math.Max(1, Math.Min(6, GI("FleeWantedLevel", fleeWantedLevel)));
        arrestFleeDistance = Math.Max(3f, GF("ArrestFleeDistance", arrestFleeDistance));

        voiceVolume = Math.Max(0, Math.Min(1000, GI("VoiceVolume", voiceVolume)));
        npcVolume = Math.Max(0f, Math.Min(1f, GF("NpcVolume", npcVolume)));
        showBlips = GB("ShowBlips", showBlips);
        blipRange = Math.Max(10f, GF("BlipRange", blipRange));
        showMessages = GB("ShowMessages", showMessages);
        sellOnlyUnarmed = GB("SellOnlyUnarmed", sellOnlyUnarmed);
        fontFamily = GS("FontFamily", fontFamily);
        fontFile = GS("FontFile", fontFile);
        dealerModel = GS("DealerModel", dealerModel);
        uiRight = GF("UiRight", uiRight);
        uiBottom = GF("UiBottom", uiBottom);

        btn[B_SELL].Name = GS("ButtonSellDrugs", btn[B_SELL].Name);
        for (int i = 0; i < 3; i++) btn[B_D0 + i].Name = GS("Button" + DrugName[i], btn[B_D0 + i].Name);
        btn[B_ROB].Name = GS("ButtonRobDealer", btn[B_ROB].Name);
        btn[B_SELL].Key = GK("KeySellDrugs", btn[B_SELL].Key);
        for (int i = 0; i < 3; i++) btn[B_D0 + i].Key = GK("Key" + DrugName[i], btn[B_D0 + i].Key);
        btn[B_ROB].Key = GK("KeyRobDealer", btn[B_ROB].Key);
        foreach (Btn b in btn) SetupButton(b);

        animPlayerSell = GA("AnimPlayerSell", animPlayerSell);
        animNpcBuy = GA("AnimNpcBuy", animNpcBuy);
        animPlayerBuy = GA("AnimPlayerBuy", animPlayerBuy);
        animDealerSell = GA("AnimDealerSell", animDealerSell);
        animWallet = GA("AnimWallet", animWallet);
        animPutAway = GA("AnimPutAway", animPutAway);
        animPutAwayPocket = GA("AnimPutAwayPocket", animPutAwayPocket);
        animWalletChest = GA("AnimDrugsOut", animWalletChest);
        animNpcSearch = GA("AnimNpcSearch", animNpcSearch);
        animNpcPunch = GA("AnimNpcPunch", animNpcPunch);
        animCopFrisk = GA("AnimCopFrisk", animCopFrisk);
        animPlayerFrisked = GA("AnimPlayerFrisked", animPlayerFrisked);
        animTrunkSearch = GA("AnimTrunkSearch", animTrunkSearch);
        animDealerIdle = GA("AnimDealerIdle", animDealerIdle);
        animDealerTalk = GA("AnimDealerTalk", animDealerTalk);
        animDealerDropCash = GA("AnimDealerDropCash", animDealerDropCash);

        speechDealerGreet = GL("SpeechDealerGreet", speechDealerGreet);
        speechDealerDone = GL("SpeechDealerDone", speechDealerDone);
        speechDealerNoSale = GL("SpeechDealerNoSale", speechDealerNoSale);
        speechDealerRobbed = GL("SpeechDealerRobbed", speechDealerRobbed);
        speechDealerFight = GL("SpeechDealerFight", speechDealerFight);
        speechBuyerAgree = GL("SpeechBuyerAgree", speechBuyerAgree);
        speechBuyerDone = GL("SpeechBuyerDone", speechBuyerDone);
        speechBuyerNoSale = GL("SpeechBuyerNoSale", speechBuyerNoSale);
        speechHostile = GL("SpeechPunch", speechHostile);
        speechRob = GL("SpeechRob", speechRob);
        speechHookerSting = GL("SpeechHookerSting", speechHookerSting);
        copVoices = GL("CopVoices", copVoices);
        animPlayerOpenTrunk = GA("AnimPlayerOpenTrunk", animPlayerOpenTrunk);
        animCopRadio = GA("AnimCopRadio", animCopRadio);
        animCopCuff = GA("AnimCopCuff", animCopCuff);
        arrestFine = Math.Max(0, GI("ArrestFine", arrestFine));

        businessModels = GL("BusinessModels", businessModels);
        hookerModels = GL("HookerModels", hookerModels);
        bumModels = GL("BumModels", bumModels);
        gangModels = GL("GangsterModels", gangModels);

        foreach (string val in iniDealers)
        {
            string[] p = val.Split(',');
            if (p.Length >= 7)
                AddDealer(p[0].Trim(), F(p[1], 0), F(p[2], 0), F(p[3], 0), F(p[4], 1), F(p[5], 1), F(p[6], 1));
        }
        if (dealers.Count == 0) AddDefaultDealers();

        // first run, or an ini from an older Pusher: write every setting out (values are kept)
        if (!exists || GI("IniVersion", 0) < VERSION)
        {
            try { WriteIni(path); } catch (Exception ex) { Log("WriteIni: " + ex.Message); }
        }
    }

    void SetupButton(Btn b)
    {
        string n = b.Name.Trim().ToUpperInvariant().Replace("_", "").Replace("-", "").Replace(" ", "");
        b.Mask = 0;
        b.Trigger = false;
        b.Label = b.Name;
        switch (n)
        {
            case "A": b.Mask = PAD_A; break;
            case "B": b.Mask = PAD_B; break;
            case "X": b.Mask = PAD_X; break;
            case "Y": b.Mask = PAD_Y; break;
            case "LB": b.Mask = 0x0100; break;
            case "RB": b.Mask = 0x0200; break;
            case "RT": b.Trigger = true; break;
            case "DPADUP": b.Mask = 0x0001; b.Label = "^"; break;
            case "DPADDOWN": b.Mask = 0x0002; b.Label = "v"; break;
            case "DPADLEFT": b.Mask = 0x0004; b.Label = "<"; break;
            case "DPADRIGHT": b.Mask = 0x0008; b.Label = ">"; break;
            case "START": b.Mask = 0x0010; break;
            case "BACK": b.Mask = 0x0020; break;
            case "LS": b.Mask = 0x0040; break;
            case "RS": b.Mask = 0x0080; break;
        }
    }

    void WriteIni(string path)
    {
        List<string> o = new List<string>();
        o.Add("; PUSHER settings. Delete this file to get every default back.");
        o.Add("IniVersion = " + VERSION);
        o.Add("");
        o.Add("; ---- compatibility (1 = on). Pusher works on its own either way.");
        o.Add("; GreetTauntRob: once that script has its Pusher section, it shows Sell Drugs and Pusher hides its own lock-on UI.");
        o.Add("GreetTauntRobCompat = " + (dialogueCompat ? 1 : 0));
        o.Add("; StopRightThere: speeding stops can turn into a trunk search (car) or a pat-down (bike).");
        o.Add("StopRightThereCompat = " + (ticketCompat ? 1 : 0));
        o.Add("");
        o.Add("; ---- PRICES");
        o.Add("; A dealer sells in packs (BuyPerPress below: 5g coke, 5g heroin, 5 pills). Price of one pack:");
        for (int i = 0; i < 3; i++) o.Add("BuyPack" + DrugName[i] + " = " + buyPrice[i]);
        o.Add("; The same pack once you have visited that dealer this many times (robbing him resets it):");
        o.Add("VisitsForRegularPrice = " + visitsForDiscount);
        for (int i = 0; i < 3; i++) o.Add("RegularPack" + DrugName[i] + " = " + regularPrice[i]);
        o.Add("; What a buyer pays you per gram / pill:");
        for (int i = 0; i < 3; i++) o.Add("Sell" + DrugName[i] + " = " + sellPrice[i]);
        o.Add("; Bulk buyers: % of buyers who want BulkAmount at once, and the % off the normal price they get:");
        o.Add("BulkBuyerChance = " + bulkBuyerChance);
        o.Add("BulkAmount = " + bulkAmount);
        o.Add("BulkDiscount = " + bulkDiscount);
        o.Add("");
        o.Add("; ---- amounts");
        o.Add("; most you can carry");
        for (int i = 0; i < 3; i++) o.Add("Max" + DrugName[i] + " = " + maxCarry[i]);
        o.Add("; one pack: how much one press buys at a dealer");
        for (int i = 0; i < 3; i++) o.Add("BuyPerPress" + DrugName[i] + " = " + buyStep[i]);
        o.Add("; what each dealer has; an emptied drug comes back after RestockMinutes (real minutes)");
        for (int i = 0; i < 3; i++) o.Add("DealerStock" + DrugName[i] + " = " + dealerStock[i]);
        o.Add("RestockMinutes = " + restockMinutes);
        o.Add("; a normal buyer wants a random amount of a drug between these (bulk buyers want BulkAmount)");
        for (int i = 0; i < 3; i++) o.Add("SellMin" + DrugName[i] + " = " + sellMin[i]);
        for (int i = 0; i < 3; i++) o.Add("SellMax" + DrugName[i] + " = " + sellMax[i]);
        o.Add("; a buyer who wants this much or more of a drug tries to rob you this % of the time");
        o.Add("BigOrderFrom = " + bigOrderFrom);
        o.Add("BigOrderRobChance = " + bigOrderRobChance);
        o.Add("; coke deals of this many grams or more show the coke pack instead of the bag");
        o.Add("CokePackFrom = " + cokePackFrom);
        o.Add("");
        o.Add("; ---- robbing a dealer (LB at his menu)");
        o.Add("; % he puts his hands up and drops his money and drugs; otherwise he pulls this weapon (Pistol, Knife, None)");
        o.Add("DealerRobComplyChance = " + dealerRobComply);
        o.Add("DealerFightWeapon = " + dealerFightWeapon);
        o.Add("DealerRobCashMin = " + dealerRobCashMin); o.Add("DealerRobCashMax = " + dealerRobCashMax);
        o.Add("; he also drops 1, 2 or all 3 drugs, each a random amount between these");
        for (int i = 0; i < 3; i++) o.Add("DealerRobMin" + DrugName[i] + " = " + dealerRobDrugMin[i]);
        for (int i = 0; i < 3; i++) o.Add("DealerRobMax" + DrugName[i] + " = " + dealerRobDrugMax[i]);
        o.Add("");
        o.Add("; ---- what can go wrong on a sale (percent)");
        o.Add("; % a buyer robs your drugs (PunchChance) or your cash (RobChance), out of 100; Bulk* when the sale is BulkRobFrom or more at once");
        o.Add("PunchChance = " + punchChance);
        o.Add("RobChance = " + robChance);
        o.Add("BulkPunchChance = " + bulkPunchChance);
        o.Add("BulkRobChance = " + bulkRobChance);
        o.Add("BulkRobFrom = " + bulkRobFrom);
        o.Add("HookerStingChance = " + hookerStingChance);
        o.Add("; cash dropped by a buyer who punched you and ran, once you kill him");
        o.Add("PunchCashMin = " + punchCashMin); o.Add("PunchCashMax = " + punchCashMax);
        o.Add("; cash a robber takes (never more than you have)");
        o.Add("RobCashMin = " + robCashMin); o.Add("RobCashMax = " + robCashMax);
        o.Add("; length of the robber's search in ms (money and drugs go at the halfway point)");
        o.Add("RobSearchMs = " + robMs);
        o.Add("; how long you stay down after a punch, and the health it costs");
        o.Add("KnockdownMs = " + knockMs); o.Add("PunchDamage = " + punchDamage);
        o.Add("; Random (knife or pistol, 50/50), Knife, Pistol or None");
        o.Add("RobberWeapon = " + robberWeapon);
        o.Add("; with RobberWeapon = Random: % the cash robber has a pistol (else a knife), % the product robber has one (else fists)");
        o.Add("CashRobberPistolChance = " + cashRobberPistolChance);
        o.Add("ProductRobberPistolChance = " + productRobberPistolChance);
        o.Add("; how far in front of you the robber stands while he goes through your pockets (metres)");
        o.Add("FriskDistance = " + S(friskDistance));
        o.Add("; squad cars sent in when a hooker turns out to be an undercover cop (0-8)");
        o.Add("StingSquadCars = " + stingSquadCars);
        o.Add("; 1 = no Sell Drugs while you are holding a weapon");
        o.Add("SellOnlyUnarmed = " + (sellOnlyUnarmed ? 1 : 0));
        o.Add("");
        o.Add("; ---- police search during a StopRightThere speeding stop");
        o.Add("; % of stops that become a search (trunk in a car, frisk on a bike), and % the officer finds your drugs");
        o.Add("SearchChance = " + searchChance); o.Add("SearchFindChance = " + searchFindChance);
        o.Add("; how long he searches before the roll (ms); crim_cuffed is held at this point (ms) so the hands stay behind the back");
        o.Add("SearchMs = " + searchMs); o.Add("CuffHoldMs = " + cuffHoldMs);
        o.Add("; in the back of the squad car: after LockpickAfterMs, RB this many times (each within LockpickWindowMs) picks the cuffs");
        o.Add("LockpickPresses = " + lockpickPresses); o.Add("LockpickWindowMs = " + lockpickWindowMs); o.Add("LockpickAfterMs = " + lockpickAfterMs);
        o.Add("RideSpeed = " + S(rideSpeed));
        o.Add("; armsup_2_searched_pose is held at this point (0-1) so the hands stay on the head; the cuffing officer stands this far behind you (m)");
        o.Add("HandsOnHeadPhase = " + S(handsOnHeadPhase)); o.Add("CuffDistance = " + S(cuffDistance));
        o.Add("; while cop_cuff plays, the officer's hips are held this far straight behind yours (bigger = further back)");
        o.Add("CuffHipDistance = " + S(cuffHipDistance));
        o.Add("; during a search: traffic from behind stops this many metres behind the squad car's rear bumper,");
        o.Add("; traffic from the front this many metres in front of your front bumper, across this half-width of road (0 = off)");
        o.Add("HaltBehindSquadCar = " + S(haltBehindCop)); o.Add("HaltAheadOfYourCar = " + S(haltAheadOfYou));
        o.Add("HaltRoadHalfWidth = " + S(haltHalfWidth));
        o.Add("; found: ms the game's own hands-up plays before armsup_loop takes over");
        o.Add("HandsUpToLoopMs = " + handsUpToLoopMs);
        o.Add("; the cuffed get-in: degrees you are turned from the car's heading when get_in_ds starts");
        o.Add("GetInHeadingOffset = " + S(getInHeadingOffset));
        o.Add("; a police officer within this many metres of a deal: 1 star, the dealer / buyer runs (0 = off)");
        o.Add("CopNearDealRange = " + copNearDealRange);
        o.Add("; caught, then you move further than this many metres: wanted level jumps to FleeWantedLevel (cops open fire)");
        o.Add("ArrestFleeDistance = " + S(arrestFleeDistance)); o.Add("FleeWantedLevel = " + fleeWantedLevel);
        o.Add("");
        o.Add("; ---- buttons: A B X Y LB RB RT DpadUp DpadDown DpadLeft DpadRight Back Start LS RS");
        o.Add("; an icon named after the button (Icons\\RB.png) is drawn if it exists");
        o.Add("ButtonSellDrugs = " + btn[B_SELL].Name);
        for (int i = 0; i < 3; i++) o.Add("Button" + DrugName[i] + " = " + btn[B_D0 + i].Name);
        o.Add("; dealer menu: rob him");
        o.Add("ButtonRobDealer = " + btn[B_ROB].Name);
        o.Add("KeySellDrugs = " + btn[B_SELL].Key);
        for (int i = 0; i < 3; i++) o.Add("Key" + DrugName[i] + " = " + btn[B_D0 + i].Key);
        o.Add("KeyRobDealer = " + btn[B_ROB].Key);
        o.Add("");
        o.Add("; ---- dealer blip (game blip number, 82 = radar_multiplayer)");
        o.Add("BlipDealer = " + dealerBlip);
        o.Add("");
        o.Add("; ---- chance (%) of the player lines SellBegin, SellAfter");
        o.Add("SellBeginChance = " + sellBeginChance); o.Add("SellAfterChance = " + sellAfterChance);
        o.Add("; 1 = keep the Dealer lines below even when locations.txt has dealers in it");
        o.Add("KeepIniDealers = " + (keepIniDealers ? 1 : 0));
        o.Add("");
        o.Add("; ---- sound and screen");
        o.Add("; player .wav volume, 0-1000 (same as GreetTauntRob's VoiceVolume; 1000 = the file as recorded)");
        o.Add("VoiceVolume = " + voiceVolume);
        o.Add("ShowBlips = " + (showBlips ? 1 : 0)); o.Add("BlipRange = " + S(blipRange)); o.Add("ShowMessages = " + (showMessages ? 1 : 0));
        o.Add("FontFamily = " + fontFamily);
        o.Add("; leave empty to use the .ttf found in scripts\\Pusher, scripts\\GreetTauntRob, scripts\\Dialogue or scripts\\StopRightThere");
        o.Add("FontFile = " + fontFile);
        o.Add("; distance of the menu from the right and bottom screen edges (pixels at 1080p)");
        o.Add("UiRight = " + S(uiRight)); o.Add("UiBottom = " + S(uiBottom));
        o.Add("DealerModel = " + dealerModel);
        o.Add("");
        o.Add("; ---- animations: animation set, animation name");
        o.Add("; (the hand-over can also be  amb@beg_standing, give_obj  /  amb@beg_standing, take_obj)");
        o.Add("AnimPlayerSell = " + J(animPlayerSell)); o.Add("AnimNpcBuy = " + J(animNpcBuy));
        o.Add("AnimPlayerBuy = " + J(animPlayerBuy)); o.Add("AnimDealerSell = " + J(animDealerSell));
        o.Add("; whoever pays takes out his wallet first");
        o.Add("AnimWallet = " + J(animWallet));
        o.Add("AnimPutAway = " + J(animPutAway));
        o.Add("AnimPutAwayPocket = " + J(animPutAwayPocket));
        o.Add("; selling: you take the drugs out of your jacket with this");
        o.Add("AnimDrugsOut = " + J(animWalletChest));
        o.Add("; the hand-over: distance between the two (m), wallet time (ms), then from the start of");
        o.Add("; buy_drugs / sell_drugs: when the drugs change hands, and when both stop (ms)");
        o.Add("DealDistance = " + S(dealDistance)); o.Add("WalletMs = " + walletMs);
        o.Add("HandoverPhase = " + S(handoverPhase)); o.Add("TradeHandoverMs = " + tradeHandoverMs); o.Add("TradeEndMs = " + tradeEndMs);
        o.Add("AnimNpcSearch = " + J(animNpcSearch)); o.Add("AnimNpcPunch = " + J(animNpcPunch));
        o.Add("AnimCopFrisk = " + J(animCopFrisk)); o.Add("AnimPlayerFrisked = " + J(animPlayerFrisked));
        o.Add("AnimTrunkSearch = " + J(animTrunkSearch));
        o.Add("; dealers stand like this while waiting, and talk with this when you walk up");
        o.Add("AnimDealerIdle = " + J(animDealerIdle)); o.Add("AnimDealerTalk = " + J(animDealerTalk));
        o.Add("; the dealer dropping his money when you rob him");
        o.Add("AnimDealerDropCash = " + J(animDealerDropCash));
        o.Add("");
        o.Add("; ---- the props handed over (OpenIV names, no .wdr)");
        o.Add("ModelCokeBag = " + drugModel[COKE]); o.Add("ModelCokePack = " + cokePackModel);
        o.Add("ModelHeroin = " + drugModel[HEROIN]); o.Add("ModelPills = " + drugModel[PILLS]);
        o.Add("ModelMoney = " + moneyModel);
        o.Add("; where each prop sits in the right hand: x, y, z offset, x, y, z rotation (degrees)");
        for (int i = 0; i < PropKey.Length; i++) o.Add(PropLine(i));
        o.Add("");
        o.Add("; ---- pickup icons (Icons\\coke_pack_pickup.png, heroin_pickup.png, pills_pickup.png) above the minimap");
        o.Add("; when you get drugs without buying them. X / Y = the middle spot (where a single icon goes), 0-1 of the screen.");
        o.Add("; Size = icon height, Arc = distance from the minimap's centre to the icons (both 0-1 of screen height).");
        o.Add("; Spread = degrees between icons along the minimap's curve. Fade in / hold / fade out in ms.");
        o.Add("PickupIconX = " + P4(pickupX));
        o.Add("PickupIconY = " + P4(pickupY));
        o.Add("PickupIconSize = " + P4(pickupSize));
        o.Add("PickupIconArc = " + P4(pickupArc));
        o.Add("PickupIconSpread = " + P4(pickupSpread));
        o.Add("PickupIconFadeInMs = " + pickupFadeIn);
        o.Add("PickupIconHoldMs = " + pickupHold);
        o.Add("PickupIconFadeOutMs = " + pickupFadeOut);
        o.Add("");
        o.Add("; ---- NPC lines: speech contexts the game plays in each NPC's own voice (his model's voice bank).");
        o.Add("; One is picked at random; if his voice has no such line the next one is tried. Nobody talks over anybody.");
        o.Add("SpeechDealerGreet = " + J(speechDealerGreet)); o.Add("SpeechDealerDone = " + J(speechDealerDone));
        o.Add("SpeechDealerNoSale = " + J(speechDealerNoSale)); o.Add("SpeechDealerRobbed = " + J(speechDealerRobbed));
        o.Add("SpeechDealerFight = " + J(speechDealerFight));
        o.Add("SpeechBuyerAgree = " + J(speechBuyerAgree)); o.Add("SpeechBuyerDone = " + J(speechBuyerDone));
        o.Add("SpeechBuyerNoSale = " + J(speechBuyerNoSale));
        o.Add("SpeechPunch = " + J(speechHostile)); o.Add("SpeechRob = " + J(speechRob));
        o.Add("SpeechHookerSting = " + J(speechHookerSting));
        o.Add("; cops say GET_OUT_OF_CAR_SHOUT, ASK_TO_OPEN_TRUNK, FOUND_NOTHING, ARREST_PED / ARREST_PLAYER /");
        o.Add("; PLACE_HANDS_ON_HEAD with their own voice; if theirs has none, one of these voice banks is used");
        o.Add("CopVoices = " + J(copVoices));
        o.Add("; police search animations");
        o.Add("AnimPlayerOpenTrunk = " + J(animPlayerOpenTrunk)); o.Add("AnimCloseBoot = " + J(animCloseBoot));
        o.Add("AnimCheckUnder = " + J(animCheckUnder)); o.Add("AnimPartnerLook = " + J(animPartnerLook));
        o.Add("AnimCopCarChat = " + J(animCopCarChat));
        o.Add("AnimArmsUp = " + J(animArmsUp)); o.Add("AnimArmsToSearched = " + J(animArmsToSearched));
        o.Add("AnimSearchedPose = " + J(animSearchedPose)); o.Add("AnimCrimCuffed = " + J(animCrimCuffed));
        o.Add("AnimGetInCuffed = " + J(animGetInCuffed)); o.Add("AnimCopCloseDoor = " + J(animCopCloseDoor));
        o.Add("AnimCopOpenDoor = " + J(animCopOpenDoor));
        o.Add("AnimCopCuff = " + J(animCopCuff));
        o.Add("; taken from your cash when you comply and get booked");
        o.Add("ArrestFine = " + arrestFine);
        o.Add("");
        o.Add("; ---- ped models that count as buyers");
        o.Add("BusinessModels = " + J(businessModels));
        o.Add("HookerModels = " + J(hookerModels));
        o.Add("BumModels = " + J(bumModels));
        o.Add("GangsterModels = " + J(gangModels));
        o.Add("");
        o.Add("; ---- Dealer = name, x, y, z, coke multiplier, heroin multiplier, pills multiplier");
        o.Add("; (only used while locations.txt has no dealers in it)");
        foreach (Dealer dl in dealers) o.Add(DealerLine(dl));
        File.WriteAllLines(path, o.ToArray());
    }

    static string DealerLine(Dealer dl)
    {
        return "Dealer = " + dl.Name + ", " + S(dl.Pos.X) + ", " + S(dl.Pos.Y) + ", " + S(dl.Pos.Z) + ", "
            + S(dl.Mult[COKE]) + ", " + S(dl.Mult[HEROIN]) + ", " + S(dl.Mult[PILLS]);
    }

    // ------------------------------------------------------------------ stash save
    void LoadStash()
    {
        string path = Path.Combine(baseDir, "stash.dat");
        if (!File.Exists(path)) return;
        string me = "v3." + episodeName;
        foreach (string line in File.ReadAllLines(path))
        {
            int eq = line.LastIndexOf('=');
            if (eq < 1) continue;
            string key = line.Substring(0, eq).Trim(), val = line.Substring(eq + 1);
            if (key == me)
            {
                string[] p = val.Split(',');
                for (int i = 0; i < 3 && i < p.Length; i++) stash[i] = Math.Max(0, Math.Min(maxCarry[i], I(p[i], 0)));
            }
            else if (key.StartsWith(me + ".visits."))
                rep[key.Substring(me.Length + 8)] = Math.Max(0, I(val, 0));
        }
    }

    void SaveStash()
    {
        Publish();
        try
        {
            string path = Path.Combine(baseDir, "stash.dat");
            List<string> o = new List<string>();
            if (File.Exists(path))
                foreach (string line in File.ReadAllLines(path))
                    if (line.StartsWith("v3.") && !line.StartsWith("v3." + episodeName + "=") && !line.StartsWith("v3." + episodeName + ".rep.") && !line.StartsWith("v3." + episodeName + ".visits.")) o.Add(line);
            o.Add("v3." + episodeName + "=" + stash[0] + "," + stash[1] + "," + stash[2]);
            foreach (KeyValuePair<string, int> kv in rep) o.Add("v3." + episodeName + ".visits." + kv.Key + "=" + kv.Value);
            File.WriteAllLines(path, o.ToArray());
        }
        catch { }
    }

    int Carried() { return stash[0] + stash[1] + stash[2]; }

    void LoadFont()
    {
        string file = null;
        if (fontFile.Length > 0)
        {
            file = Path.IsPathRooted(fontFile) ? fontFile : Path.Combine(baseDir, fontFile);
            if (!File.Exists(file)) file = null;
        }
        if (file == null)
        {
            string scripts = Path.Combine(Game.InstallFolder, "scripts");
            string[] dirs = { baseDir, Path.Combine(scripts, "GreetTauntRob"), Path.Combine(scripts, "Dialogue"),
                Path.Combine(scripts, "StopRightThere") };
            foreach (string dir in dirs)
            {
                if (!Directory.Exists(dir)) continue;
                string[] f = Directory.GetFiles(dir, "*.ttf");
                if (f.Length > 0) { file = f[0]; break; }
            }
        }
        if (file != null) AddFontResourceEx(file, 0x10, IntPtr.Zero);   // private to this process
    }

    Texture LoadTexture(string file)
    {
        try
        {
            string path = Path.Combine(Path.Combine(baseDir, "Icons"), file);
            if (!File.Exists(path)) return null;
            return new Texture(File.ReadAllBytes(path));
        }
        catch (Exception ex) { Log("Icon " + file + ": " + ex.Message); return null; }
    }

    // 20 copies of an icon at 5%, 10% ... 100% opacity, made once at load (ReFuel's ColorMatrix method)
    Texture[] LoadFaded(string file)
    {
        try
        {
            string path = Path.Combine(Path.Combine(baseDir, "Icons"), file);
            if (!File.Exists(path)) return null;
            Texture[] fade = new Texture[PickupFadeLevels];
            using (Bitmap src = new Bitmap(path))
            {
                for (int i = 0; i < PickupFadeLevels; i++)
                {
                    using (Bitmap dst = new Bitmap(src.Width, src.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    {
                        using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(dst))
                        {
                            System.Drawing.Imaging.ColorMatrix cm = new System.Drawing.Imaging.ColorMatrix();
                            cm.Matrix33 = (i + 1) / (float)PickupFadeLevels;
                            System.Drawing.Imaging.ImageAttributes ia = new System.Drawing.Imaging.ImageAttributes();
                            ia.SetColorMatrix(cm);
                            g.DrawImage(src, new Rectangle(0, 0, src.Width, src.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                        }
                        using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                        {
                            dst.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            fade[i] = new Texture(ms.ToArray());
                        }
                    }
                }
            }
            return fade;
        }
        catch (Exception ex) { Log("Icon fade " + file + ": " + ex.Message); return null; }
    }

    void LoadIcons()
    {
        for (int i = 0; i < 3; i++) drugIcon[i] = LoadTexture(DrugIconFile[i]);
        packIcon = LoadTexture("coke_pack.png");
        for (int i = 0; i < 3; i++) { pickupIcon[i] = LoadTexture(PickupIconFile[i]); pickupFade[i] = LoadFaded(PickupIconFile[i]); }
        foreach (Btn b in btn) b.Icon = LoadTexture(b.Name.Trim() + ".png");
    }

    void Publish()
    {
        AppDomain d = AppDomain.CurrentDomain;
        d.SetData("Pusher.Alive", Game.GameTime);          // heartbeat: GreetTauntRob only links up while this is fresh
        d.SetData("Pusher.GtrLink", dialogueCompat);
        d.SetData("Pusher.SrtLink", ticketCompat);
        d.SetData("Pusher.Carrying", Carried() > 0);
        d.SetData("Pusher.Busy", mode == M_SELL || mode == M_DEALER || scene != S_NONE);
    }

    void SetSearchState(int value)
    {
        searchState = value;
        AppDomain.CurrentDomain.SetData("Pusher.SearchState", value);
    }

    // ------------------------------------------------------------------ input
    void ReadPad()
    {
        padButtons = 0;
        ltHeld = false; rtHeld = false; stickMoved = false;
        if (padOk)
        {
            try
            {
                XState st;
                if (XInputGetState(0, out st) == 0)
                {
                    padButtons = st.Pad.Buttons;
                    ltHeld = st.Pad.LT > 40;
                    rtHeld = st.Pad.RT > 60;
                    stickMoved = Math.Abs((int)st.Pad.LX) > 18000 || Math.Abs((int)st.Pad.LY) > 18000;
                }
            }
            catch { padOk = false; }
        }
        aimHeld = ltHeld;
        try { if (Game.isGameKeyPressed(GameKey.Aim)) aimHeld = true; } catch { }

        bool rtWas = rtPrev;
        rtPrev = rtHeld;
        if (rtHeld && !rtWas && (mode == M_DEALER || mode == M_SELL)) SwallowAttack();
        foreach (Btn b in btn)
        {
            bool now = (b.Mask != 0 && (padButtons & b.Mask) != 0) || (b.Trigger && rtHeld)
                || (b.Key != Keys.None && Game.isKeyPressed(b.Key));
            b.Pressed = now && !b.Down;
            b.Down = now;
        }
    }

    // any attack or movement input: used to break out of a robbery or a pat-down
    bool BreakInput()
    {
        if (rtHeld || stickMoved) return true;
        if ((padButtons & (PAD_A | PAD_B | PAD_X | PAD_Y)) != 0) return true;
        try
        {
            return Game.isGameKeyPressed(GameKey.Attack) || Game.isGameKeyPressed(GameKey.Jump)
                || Game.isGameKeyPressed(GameKey.Sprint)
                || Game.isGameKeyPressed(GameKey.MoveForward) || Game.isGameKeyPressed(GameKey.MoveBackward)
                || Game.isGameKeyPressed(GameKey.MoveLeft) || Game.isGameKeyPressed(GameKey.MoveRight);
        }
        catch { return false; }
    }

    void OnKeyDown(object sender, GTA.KeyEventArgs e)
    {
        if (!inited) return;

    }

    static string LocationsHeader()
    {
        string n = Environment.NewLine;
        return "; PUSHER dealer locations. One dealer per line:" + n
            + ";   (x, y, z, heading), (ped model)" + n
            + "; Every dealer sells coke, heroin and pills." + n
            + "; Leave the model empty  ()  for a door dealer: no ped, the dealer menu opens" + n
            + "; when you stand at that spot (put it at a door you can't go through)." + n
            + "; Lines starting with ; are ignored." + n + n;
    }

    // locations.txt: these dealers replace the ini ones (unless KeepIniDealers = 1)
    void LoadLocations()
    {
        string path = Path.Combine(baseDir, "locations.txt");
        if (!File.Exists(path))
        {
            try { File.WriteAllText(path, LocationsHeader()); } catch { }
            return;
        }
        List<Dealer> found = new List<Dealer>();
        int lineNo = 0;
        foreach (string raw in File.ReadAllLines(path))
        {
            lineNo++;
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
            // pull out the (...) groups
            List<string> parts = new List<string>();
            int i = 0;
            while (i < line.Length)
            {
                int a = line.IndexOf('(', i);
                if (a < 0) break;
                int b = line.IndexOf(')', a + 1);
                if (b < 0) break;
                parts.Add(line.Substring(a + 1, b - a - 1).Trim());
                i = b + 1;
            }
            if (parts.Count < 1) { Log("locations.txt line " + lineNo + ": no (x, y, z)"); continue; }
            string[] c = parts[0].Split(',');
            if (c.Length < 3) { Log("locations.txt line " + lineNo + ": needs x, y, z"); continue; }

            Dealer dl = new Dealer();
            dl.Pos = new Vector3(F(c[0], 0), F(c[1], 0), F(c[2], 0));
            dl.Heading = c.Length >= 4 ? F(c[3], -1f) : -1f;
            dl.Exact = true;
            string model = (parts.Count >= 2 ? parts[1] : "").Trim();
            string up = model.ToUpperInvariant();
            dl.Door = model.Length == 0 || up == "MODEL" || up == "NONE" || up == "DOOR";
            dl.Model = dl.Door ? "" : model;
            // a stable name (relationships are saved by it) and his own prices
            dl.Name = "dealer@" + (int)Math.Round(dl.Pos.X) + "," + (int)Math.Round(dl.Pos.Y);
            int seed = (int)Math.Round(dl.Pos.X * 7f + dl.Pos.Y * 13f);
            Random pr = new Random(seed);
            for (int k = 0; k < 3; k++)
                dl.Mult[k] = (float)Math.Round(0.85 + pr.NextDouble() * 0.40, 2);
            found.Add(dl);
        }
        if (found.Count == 0) return;
        if (!keepIniDealers) dealers.Clear();
        dealers.AddRange(found);
    }

    // ------------------------------------------------------------------ main loop
    void OnTick(object sender, EventArgs e)
    {
        uiPending = new List<SpriteCmd>();
        TickBody();
        try { DrawUi(); } catch (Exception ex) { Log("DrawUi: " + ex.Message); }
        uiCmds = uiPending;
    }

    void TickBody()
    {
        try
        {
            if (!inited) Init();
            int now = Game.GameTime;
            UpdateAudio(now);

            Ped me = Player.Character;
            if (me == null || !Exists(me)) return;
            if (!me.isAlive)
            {
                if (scene == S_SEARCH) SetSearchState(4);
                EndScene();
                CloseMenu();
                arrestActive = false;
                Publish();
                return;
            }

            // during a mission Pusher stays out of the way completely: no menus, no scenes, no dealers or
            // blips. Whatever was running when the mission started is dropped on the spot.
            bool onMission = false;
            try { onMission = Player.isOnMission || GTA.Native.Function.Call<bool>("GET_MISSION_FLAG"); } catch { }
            if (onMission)
            {
                if (!missionPaused) { missionPaused = true; PauseForMission(me); }
                Publish();
                return;
            }
            if (missionPaused) { missionPaused = false; Log("Mission over: Pusher is back on"); }

            // DynamicScenarios: the player stole a drug shipment (a shared counter it bumps; checked every frame)
            try
            {
                object so = AppDomain.CurrentDomain.GetData("Pusher.ShipmentStolen");
                int sc = so is int ? (int)so : 0;
                if (shipmentSeen < 0) shipmentSeen = sc;          // whatever was there before this load doesn't count
                else if (sc != shipmentSeen) { shipmentSeen = sc; pendingGiveMax = true; }
            }
            catch { }

            if (pendingGiveMax && inited)
            {
                pendingGiveMax = false;
                for (int i = 0; i < 3; i++) stash[i] = maxCarry[i];
                SaveStash();
                // the three icons for a stolen shipment are drawn by DynamicScenarios itself, after the steal anim
            }

            hudMoney = Player.Money;
            hudInVehicle = me.isInVehicle();

            ReadPad();
            if (controlBackAt != 0 && (now >= controlBackAt || now < controlBackAt - 5000))
            {
                controlBackAt = 0;
                if (!playerLocked) { try { Player.CanControlCharacter = true; } catch { } }
            }
            UpdateArmedDealers(now);
            UpdateAnimChecks(now);
            UpdateSting(me, now);
            if (CopNearDeal(me, now)) { Publish(); return; }
            if (propTest != null && (now >= propTestUntil || now < propTestUntil - 60000)) DropProp(ref propTest);
            ProcessRequests(me, now);
            UpdateDealers(me, now);
            UpdateThieves(me, now);
            UpdateLoot(me, now);
            UpdateHeld(now);
            UpdateSquads(me, now);
            UpdateArrest(me, now);
            if (now - lastCleanup > 20000 || now < lastCleanup) Cleanup(now);

            if (scene != S_NONE)
            {
                try
                {
                switch (scene)
                {
                    case S_DEAL: TickDeal(me, now); break;
                    case S_PUNCH: TickPunch(me, now); break;
                    case S_ROB: TickRob(me, now); break;
                    case S_SEARCH: TickSearch(me, now); break;
                    case S_BUY: TickBuy(me, now); break;
                    case S_DEALROB: TickDealRob(me, now); break;
                    case S_APPROACH: TickApproach(me, now); break;
                }
                }
                catch (Exception ex)
                {
                    // never leave the player frozen in a scene
                    Log("Scene " + scene + " failed: " + ex);
                    try { me.Task.ClearAll(); } catch { }
                    try { EndScene(); } catch { }
                    if (playerLocked) LockPlayer(false);
                }
                Publish();
                return;
            }

            if (Player.isOnMission) { CloseMenu(); Publish(); return; }
            TickMenus(me, now);
            Publish();
        }
        catch (Exception ex) { Log("Tick: " + ex); }
    }

    void ProcessRequests(Ped me, int now)
    {
        if (pendingOpenDealer != null)
        {
            Ped p = pendingOpenDealer;
            pendingOpenDealer = null;
            Dealer dl = DealerOf(p);
            if (dl != null && scene == S_NONE && !me.isInVehicle() && Player.WantedLevel == 0) StartApproach(p, dl, null, me, now);
        }
        if (pendingRobStart != null)
        {
            Ped p = pendingRobStart;
            pendingRobStart = null;
            Dealer dl = DealerOf(p);
            if (dl != null)
            {
                rep[dl.Name] = 0;                 // regular status gone
                dl.VisitCounted = true;
                SaveStash();
                if (curDealer == dl) CloseMenu();
                dl.Ped = null;                    // spot empty for a while, GreetTauntRob has him now
                dl.NextSpawnTry = now + 600000;
                RobbedDealer rd = new RobbedDealer();
                rd.Ped = p; rd.Until = now + 60000;
                robbedDealers.Add(rd);
                Hold(p, 90000);
                if (pendingRobFight && Alive(p))
                {
                    // he doesn't give it up: out comes the gun
                    GiveWeapon(p, dealerFightWeapon);
                    try { p.Task.ClearAll(); p.Task.FightAgainst(me); } catch { }
                    Say(p, speechDealerFight);
                    AddArmedDealer(p);
                }
            }
        }
        while (pendingDrops.Count > 0)
        {
            Ped p = pendingDrops[0];
            pendingDrops.RemoveAt(0);
            try { if (p != null && Exists(p)) DropDealerStash(p, now); } catch (Exception ex) { Log("DealerDrop: " + ex.Message); }
        }
        for (int i = robbedDealers.Count - 1; i >= 0; i--)
            if (now >= robbedDealers[i].Until || now < robbedDealers[i].Until - 120000) robbedDealers.RemoveAt(i);
        if (pendingSell != null)
        {
            Ped p = pendingSell;
            string title = pendingSellTitle;
            pendingSell = null; pendingSellTitle = null;
            if (scene == S_NONE && Alive(p) && !me.isInVehicle() && !IsDealer(p) && !IsThief(p) && !GetOffer(p).Done)
                StartApproach(p, null, title, me, now);
        }
        if (pendingSearch)
        {
            pendingSearch = false;
            searchCop = pendingSearchCop; searchVeh = pendingSearchVeh;
            pendingSearchCop = null; pendingSearchVeh = null;
            if (scene == S_NONE && Alive(searchCop) && searchVeh != null && Exists(searchVeh)) StartSearch(me, now);
            else SetSearchState(2);
        }
    }

    bool Alive(Ped p)
    {
        try { return p != null && Exists(p) && p.isAlive; } catch { return false; }
    }

    void CloseMenu()
    {
        dealerLockOn = false;
        if (mode == M_NONE) return;
        mode = M_NONE; curDealer = null; curBuyer = null; curOffer = null;
    }

    void BeginScene(int kind, Ped ped, int now)
    {
        CloseMenu();
        scene = kind; scenePhase = 0; sceneStart = now; phaseStart = now;
        scenePed = ped; sceneFlag = false; lastAnim = 0;
    }

    void NextPhase(int phase, int now)
    {
        scenePhase = phase; phaseStart = now; sceneFlag = false; lastAnim = 0;
    }

    bool missionPaused;

    void PauseForMission(Ped me)
    {
        Log("Mission started: Pusher paused");
        try
        {
            CloseMenu();
            if (scene == S_SEARCH) EndSearch(4);
            else if (scene != S_NONE)
            {
                Ped sp = scenePed;
                try { me.Task.ClearAll(); } catch { }
                EndScene();
                if (Alive(sp)) Release(sp);
            }
            if (playerLocked) LockPlayer(false);
            if (stingPed != null) { if (Alive(stingPed)) Release(stingPed); stingPed = null; }
            stingStage = 0;
            foreach (Dealer dl in dealers)
            {
                if (dl.Blip != null) { try { dl.Blip.Delete(); } catch { } dl.Blip = null; }
                if (dl.Ped != null) { try { if (Exists(dl.Ped)) dl.Ped.NoLongerNeeded(); } catch { } dl.Ped = null; }
            }
        }
        catch (Exception ex) { Log("Mission pause: " + ex.Message); }
    }

    void EndScene()
    {
        if (playerLocked) LockPlayer(false);
        copChoice = false;
        scene = S_NONE; scenePed = null; sceneOffer = null;
        DropProp(ref sceneProp);
        DropProp(ref sceneMoney);
    }

    // ------------------------------------------------------------------ props (the drugs that change hands)
    string PropModel(int drug, int qty)
    {
        return (drug == COKE && qty >= cokePackFrom) ? cokePackModel : drugModel[drug];
    }

    int PropIndex(int drug, int qty)
    {
        return (drug == COKE && qty >= cokePackFrom) ? 3 : drug;
    }

    Texture IconFor(int drug, int qty)
    {
        if (drug == COKE && qty >= cokePackFrom && packIcon != null) return packIcon;
        return drugIcon[drug];
    }

    string PropLine(int i)
    {
        float[] f = propPlace[i];
        return PropKey[i] + " = " + S(f[0]) + ", " + S(f[1]) + ", " + S(f[2]) + ", " + S(f[3]) + ", " + S(f[4]) + ", " + S(f[5]);
    }

    void LoadPropPlaces()
    {
        for (int i = 0; i < PropKey.Length; i++)
        {
            string[] v = GL(PropKey[i], null);
            if (v == null) continue;
            for (int k = 0; k < 6 && k < v.Length; k++) propPlace[i][k] = F(v[k], propPlace[i][k]);
        }
    }

    // a model that loads: the one asked for, or (for coke) the heroin bag so something is always in the hand
    // GTA IV model hash (Jenkins one-at-a-time on the lower-case name). Worked out here instead of asking
    // the game, which crashed once mid-deal (GetHashKey access violation).
    static int ModelHash(string name)
    {
        unchecked
        {
            uint h = 0;
            foreach (char c in name.Trim().ToLowerInvariant())
            {
                h += (byte)c;
                h += h << 10;
                h ^= h >> 6;
            }
            h += h << 3;
            h ^= h >> 11;
            h += h << 15;
            return (int)h;
        }
    }

    readonly Dictionary<string, int> propHashes = new Dictionary<string, int>();   // name -> hash, 0 = not in the game

    int PropHash(string name)
    {
        string key = name.Trim().ToLowerInvariant();
        int h;
        if (propHashes.TryGetValue(key, out h)) return h;
        h = 0;
        try { int x = ModelHash(key); if (new Model(x).isValid) h = x; } catch { }
        if (h == 0) Log("Prop model not found: " + key);
        propHashes[key] = h;
        return h;
    }

    // the model asked for; the coke pack falls back to cj_coke_pack, then the coke bag; coke falls back to the heroin bag
    Model PropModelFor(string name, int drug)
    {
        int h = PropHash(name);
        if (h == 0 && drug == COKE && name.Trim().ToLowerInvariant() == cokePackModel.Trim().ToLowerInvariant())
        {
            h = PropHash("cj_coke_pack");
            if (h == 0) h = PropHash(drugModel[COKE]);
        }
        if (h == 0 && drug == COKE) h = PropHash(drugModel[HEROIN]);
        if (h == 0) return Model.Null;
        return new Model(h);
    }

    void AttachProp(GTA.Object o, Ped holder, int idx)
    {
        float[] f = propPlace[idx];
        const float D2R = (float)(Math.PI / 180.0);
        try
        {
            GTA.Native.Function.Call("ATTACH_OBJECT_TO_PED", o, holder, (int)Bone.RightHand, f[0], f[1], f[2], f[3] * D2R, f[4] * D2R, f[5] * D2R, 0);
        }
        catch { o.AttachToPed(holder, Bone.RightHand, new Vector3(f[0], f[1], f[2]), new Vector3(f[3] * D2R, f[4] * D2R, f[5] * D2R)); }
    }

    // the cash (cssmlmoneywad) in someone's right hand
    GTA.Object MakeMoney(Ped holder)
    {
        try
        {
            if (holder == null || !Exists(holder)) return null;
            int h = PropHash(moneyModel);
            if (h == 0) return null;
            GTA.Object o = World.CreateObject(new Model(h), holder.Position);
            if (o == null || !Exists(o)) { Log("Prop did not spawn: " + moneyModel); return null; }
            try { o.Collision = false; } catch { }
            AttachProp(o, holder, PROP_MONEY);
            return o;
        }
        catch (Exception ex) { Log("Money prop: " + ex.Message); return null; }
    }

    void HandMoney(GTA.Object o, Ped to)
    {
        try
        {
            if (o != null && Exists(o) && to != null && Exists(to))
            {
                try { o.Detach(); } catch { }
                AttachProp(o, to, PROP_MONEY);
            }
        }
        catch { }
    }

    // where an animation is, in frames (30 a second): from its own clock, or from when it was started
    readonly Dictionary<string, float> animTotals = new Dictionary<string, float>();
    float AnimTotalMs(Ped p, string[] a)
    {
        string key = a[0] + "/" + a[1];
        float v;
        if (animTotals.TryGetValue(key, out v)) return v;
        v = -1f;
        try
        {
            GTA.Native.Pointer pt = new GTA.Native.Pointer(typeof(float));
            GTA.Native.Function.Call("GET_CHAR_ANIM_TOTAL_TIME", p, a[0], a[1], pt);
            float got = Convert.ToSingle(pt.Value);
            if (got > 0f) v = got < 30f ? got * 1000f : got;      // seconds or ms
        }
        catch { }
        if (v > 0f) { animTotals[key] = v; Log("Animation length " + key + ": " + (int)v + " ms (" + (int)(v * 0.03f) + " frames)"); }
        return v;
    }

    float AnimFrame(Ped p, string[] a, int startedAt, int now)
    {
        float ph = AnimPhase(p, a);
        if (ph > 0.001f && ph <= 1.01f)
        {
            float tot = AnimTotalMs(p, a);
            if (tot > 0f) return ph * tot * 0.03f;
        }
        return (now - startedAt) * 0.03f;
    }

    // a one-shot animation has reached its end (or is no longer playing)
    bool AnimDone(Ped p, string[] a, int t, int maxMs)
    {
        if (t >= maxMs) return true;
        if (t > 600 && !AnimPlaying(p, a)) return true;
        float ph = AnimPhase(p, a);
        return ph >= 0.97f && ph <= 1.01f;
    }

    GTA.Object MakeProp(int drug, int qty, Ped holder)
    {
        try
        {
            string name = PropModel(drug, qty);
            if (name == null || name.Trim().Length == 0 || holder == null || !Exists(holder)) return null;
            Model m = PropModelFor(name, drug);
            if (m.Hash == 0) return null;
            GTA.Object o = World.CreateObject(m, holder.Position);
            if (o == null || !Exists(o)) { Log("Prop did not spawn: " + name); return null; }
            try { o.Collision = false; } catch { }
            AttachProp(o, holder, PropIndex(drug, qty));
            return o;
        }
        catch (Exception ex) { Log("Prop: " + ex.Message); return null; }
    }

    void HandProp(GTA.Object o, Ped to)
    {
        try
        {
            if (o != null && Exists(o) && to != null && Exists(to))
            {
                try { o.Detach(); } catch { }
                AttachProp(o, to, PropIndex(sceneDrug, sceneQty));
            }
        }
        catch { }
    }

    // KeyPropTest: re-read the Prop lines from Pusher.ini, then hold the next prop for a few seconds
    void PropTest(Ped me, int now)
    {
        DropProp(ref propTest);
        try
        {
            string path = Path.Combine(baseDir, "Pusher.ini");
            if (File.Exists(path))
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    if (key.StartsWith("prop")) ini[key] = line.Substring(eq + 1).Trim();
                }
            LoadPropPlaces();
        }
        catch (Exception ex) { Log("PropTest ini: " + ex.Message); }
        int idx = propTestIdx % PropKey.Length;
        propTestIdx++;
        propTestKind = idx;
        if (idx == PROP_MONEY) propTest = MakeMoney(me);
        else
        {
            int drug = idx == 3 ? COKE : idx;
            int qty = idx == 3 ? cokePackFrom : 1;
            propTest = MakeProp(drug, qty, me);
        }
        propTestUntil = now + 45000;
        ShowPropTest();
    }

    int propTestKind;

    void ShowPropTest()
    {
        float[] f = propPlace[propTestKind];
        try { Game.DisplayText(PropLine(propTestKind) + (propTest == null ? "  - model not found" : "") + "   (7/8/9 turn it, key again = next)", 6000); } catch { }
    }

    // F4 / F5 / F6: the cash / the coke pack / the heroin bag in your right hand, the way it sits in the deals.
    // The same key again puts it away and saves its line in Pusher.ini. 7 / 8 / 9 turn it 15 degrees about its
    // X / Y / Z (Shift + 7 / 8 / 9 turns it back), saved on every turn.
    void ToggleTune(Ped me, int kind)
    {
        bool same = tuneObj != null && tuneKind == kind;
        if (tuneObj != null) { DropProp(ref tuneObj); SaveTune(); }
        if (same) { tuneKind = -1; try { Game.DisplayText(PropKey[kind] + " saved", 2500); } catch { } return; }
        tuneKind = kind;
        if (kind == PROP_MONEY) tuneObj = MakeMoney(me);
        else if (kind == 3) tuneObj = MakeProp(COKE, cokePackFrom, me);
        else tuneObj = MakeProp(kind, 1, me);
        string model = kind == PROP_MONEY ? moneyModel : PropModel(kind == 3 ? COKE : kind, kind == 3 ? cokePackFrom : 1);
        Log("Prop line-up: " + PropKey[kind] + " (" + model + ")" + (tuneObj == null ? " - did not spawn" : ""));
        ShowTune(tuneObj == null ? "  - " + model + " did not spawn" : "");
    }

    void TurnTune(int axis, int sign)
    {
        if (tuneObj == null || !Exists(tuneObj) || tuneKind < 0) return;
        float[] f = propPlace[tuneKind];
        float v = f[3 + axis] + TuneStep * sign;
        while (v < 0f) v += 360f;
        while (v >= 360f) v -= 360f;
        f[3 + axis] = v;
        try { tuneObj.Detach(); } catch { }
        try { AttachProp(tuneObj, Player.Character, tuneKind); } catch { }
        SaveTune();
        ShowTune("");
    }

    void SaveTune()
    {
        if (tuneKind < 0) return;
        float[] f = propPlace[tuneKind];
        try { ini[PropKey[tuneKind].ToLowerInvariant()] = S(f[0]) + ", " + S(f[1]) + ", " + S(f[2]) + ", " + S(f[3]) + ", " + S(f[4]) + ", " + S(f[5]); } catch { }
        try { WriteIni(Path.Combine(baseDir, "Pusher.ini")); } catch (Exception ex) { Log("Prop save: " + ex.Message); }
    }

    void ShowTune(string extra)
    {
        if (tuneKind < 0) return;
        try { Game.DisplayText(PropLine(tuneKind) + extra + "   (7 / 8 / 9 turn, Shift = back)", 8000); } catch { }
    }

    void DropProp(ref GTA.Object o)
    {
        try { if (o != null && Exists(o)) o.Delete(); } catch { }
        o = null;
    }

    // take an ambient NPC under script control / give him back to the game
    void Grab(Ped p)
    {
        // the game crashes (protected memory) if the ped is already gone or is already held as a mission
        // character (by this or another script), so only a live, free ped is made one
        if (p == null || !Exists(p)) return;
        bool held = false;
        try { if (!p.isAlive) return; held = p.isRequiredForMission; } catch { return; }
        try { if (!held) p.BecomeMissionCharacter(); p.BlockPermanentEvents = true; p.Task.ClearAll(); } catch { }
        NoRagdoll(p, true);
    }

    void Release(Ped p)
    {
        NoRagdoll(p, false);
        try { if (p != null && Exists(p)) { p.BlockPermanentEvents = false; p.NoLongerNeeded(); } } catch { }
    }

    // bumping into the player (or him falling over) must not knock an NPC over mid-scene
    class RagLock { public Ped Ped; public int Until; }
    readonly List<RagLock> ragLocks = new List<RagLock>();

    void NoRagdoll(Ped p, bool on)
    {
        try { if (p != null && Exists(p)) p.PreventRagdoll = on; } catch { }
        if (!on) for (int i = ragLocks.Count - 1; i >= 0; i--) if (ragLocks[i].Ped == p) ragLocks.RemoveAt(i);
    }

    // no ragdoll for a few seconds (he punched / robbed you and runs off), then normal again
    void NoRagdollFor(Ped p, int ms)
    {
        NoRagdoll(p, true);
        RagLock r = new RagLock();
        r.Ped = p; r.Until = Game.GameTime + ms;
        ragLocks.Add(r);
    }

    void Hold(Ped p, int ms)
    {
        Held h = new Held();
        h.Ped = p; h.Until = Game.GameTime + ms;
        held.Add(h);
    }

    void UpdateHeld(int now)
    {
        for (int i = ragLocks.Count - 1; i >= 0; i--)
        {
            RagLock r = ragLocks[i];
            if (now >= r.Until || now < r.Until - 60000)
            {
                ragLocks.RemoveAt(i);
                try { if (r.Ped != null && Exists(r.Ped)) r.Ped.PreventRagdoll = false; } catch { }
            }
        }
        for (int i = held.Count - 1; i >= 0; i--)
        {
            Held h = held[i];
            if (!Alive(h.Ped) || now >= h.Until || now < h.Until - 600000)
            {
                Release(h.Ped);
                held.RemoveAt(i);
            }
        }
    }

    // ------------------------------------------------------------------ animations
    bool Anim(Ped p, string[] a)
    {
        if (a == null || a.Length < 2 || a[0].Length == 0 || a[1].Length == 0) return false;
        if (badSets.ContainsKey(a[0])) return false;
        try
        {
            AnimationSet s;
            if (!animSets.TryGetValue(a[0], out s)) { s = new AnimationSet(a[0]); animSets[a[0]] = s; }
            p.Task.PlayAnimation(s, a[1], 8f, AnimationFlags.None);
            if (!s.isInMemory)
            {
                badSets[a[0]] = true;
                Log("Animation set did not load: " + a[0]);
                return false;
            }
            return true;
        }
        catch (Exception ex) { Log("Anim " + a[0] + " / " + a[1] + ": " + ex.Message); return false; }
    }

    bool AnimPlaying(Ped p, string[] a)
    {
        try
        {
            AnimationSet s;
            if (a == null || a.Length < 2 || !animSets.TryGetValue(a[0], out s)) return false;
            return p.Animation.isPlaying(s, a[1]);
        }
        catch { return false; }
    }

    // keeps a one-shot animation going for as long as the phase lasts
    void LoopAnim(Ped p, string[] a, int now)
    {
        if (now - lastAnim < 900 && lastAnim != 0) return;
        if (lastAnim != 0 && AnimPlaying(p, a)) return;
        Anim(p, a);
        lastAnim = now;
    }

    // ------------------------------------------------------------------ dealers
    const float DealerSpawnRange = 20f;      // the dealer himself only appears within this many metres (the blip shows from BlipRange)

    void UpdateDealers(Ped me, int now)
    {
        Vector3 pp = me.Position;
        foreach (Dealer dl in dealers)
        {
            float dx = pp.X - dl.Pos.X, dy = pp.Y - dl.Pos.Y;
            float d2 = dx * dx + dy * dy;
            bool has = dl.Ped != null && Exists(dl.Ped);
            if (d2 > 25f * 25f) dl.VisitCounted = false;      // left: the next purchase is a new visit
            UpdateStock(dl);
            UpdateBlip(dl, d2);

            if (has && (d2 > 200f * 200f || !dl.Ped.isAlive))
            {
                if (!dl.Ped.isAlive) dl.NextSpawnTry = now + 120000;
                try { dl.Ped.NoLongerNeeded(); } catch { }
                dl.Ped = null;
                continue;
            }
            if (!has && !dl.Door && d2 < DealerSpawnRange * DealerSpawnRange && now >= dl.NextSpawnTry)
            {
                dl.NextSpawnTry = now + 5000;
                SpawnDealer(dl);
            }
            if (has) { DealerRoom(dl, me, d2, now); KeepDealerPose(dl, now, pp); }
        }
    }

    // Inside a building the game only draws a ped that belongs to a room being drawn. A dealer spawned by the
    // script belongs to no room (or the wrong one), so indoors he was only seen through a doorway, from behind.
    // While you are inside near him he is put in your room, so he is drawn like everyone else.
    int RoomKey(Ped p)
    {
        try
        {
            GTA.Native.Pointer k = new GTA.Native.Pointer(typeof(int));
            GTA.Native.Function.Call("GET_KEY_FOR_CHAR_IN_ROOM", p, k);
            return Convert.ToInt32(k.Value);
        }
        catch { return 0; }
    }

    void DealerRoom(Dealer dl, Ped me, float d2, int now)
    {
        if (now - dl.RoomCheckAt < 500 && now >= dl.RoomCheckAt) return;
        dl.RoomCheckAt = now;
        Ped p = dl.Ped;
        if (!Alive(p) || d2 > DealerSpawnRange * DealerSpawnRange) return;
        try
        {
            if (Math.Abs(p.Position.Z - me.Position.Z) > 3f) return;     // another floor
            int mine = RoomKey(me);
            if (mine == 0) return;                                        // you are outside
            if (RoomKey(p) != mine) GTA.Native.Function.Call("SET_ROOM_FOR_CHAR_BY_KEY", p, mine);
        }
        catch { }
    }

    // arms crossed while waiting; turns and talks while the player deals with him; back to the stance after
    void KeepDealerPose(Dealer dl, int now, Vector3 player)
    {
        Ped p = dl.Ped;
        if (!Alive(p)) return;
        if (scene == S_APPROACH && approachDl == dl) { dl.WasBusy = true; return; }
        if (dl.Returning)
        {
            // walking back to his spot after a deal
            bool there = true;
            try { there = p.Position.DistanceTo(dl.Home) < 0.8f; } catch { }
            if (!there && now - dl.ReturnSince < 15000 && now >= dl.ReturnSince) return;
            dl.Returning = false;
            dl.WasBusy = true;            // below: face the saved way, then fold the arms
        }
        bool aimed = false;
        try
        {
            aimed = GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_AIMING_AT_CHAR", Player, p)
                 || GTA.Native.Function.Call<bool>("IS_PLAYER_TARGETTING_CHAR", Player, p);
        }
        catch { }
        if (aimed && !(curDealer == dl && mode == M_DEALER)) { dl.WasBusy = true; return; }
        bool busy = curDealer == dl && (mode == M_DEALER || scene == S_BUY);
        if (busy)
        {
            dl.WasBusy = true;
            if (dl.TalkAt != 0 && now >= dl.TalkAt)
            {
                dl.TalkAt = 0;
                Anim(p, animDealerTalk);
            }
            return;
        }
        if (dl.WasBusy)
        {
            // the player walked off: face the saved way again, then fold the arms
            dl.WasBusy = false;
            dl.TalkAt = 0;
            try
            {
                p.Task.ClearAll();
                if (dl.Heading >= 0f) GTA.Native.Function.Call("TASK_ACHIEVE_HEADING", p, dl.Heading);
            }
            catch { }
            dl.IdleAt = now + 1500;
            return;
        }
        if (now < dl.IdleAt && dl.IdleAt - now < 60000) return;
        dl.IdleAt = now + 1000;
        if (!AnimPlaying(p, animDealerIdle)) Anim(p, animDealerIdle);
    }

    void SpawnDealer(Dealer dl)
    {
        try
        {
            Vector3 pos = dl.Pos;
            if (dl.Exact) pos = new Vector3(pos.X, pos.Y, pos.Z - 1.0f);    // saved at the player's middle
            else
            {
                pos = World.GetNextPositionOnPavement(dl.Pos);
                if (pos.DistanceTo(dl.Pos) > 120f) pos = World.GetGroundPosition(dl.Pos);
            }
            Ped p = null;
            string want = dl.Model != null && dl.Model.Trim().Length > 0 ? dl.Model.Trim() : dealerModel;
            Model m = new Model(ModelHash(want));
            if (!m.isValid)
            {
                Log("Dealer model not found: " + want + " (" + dl.Name + "), using " + dealerModel);
                m = new Model(ModelHash(dealerModel));
            }
            try { p = World.CreatePed(m, pos, RelationshipGroup.Dealer); } catch { }
            if (p == null || !Exists(p)) p = World.CreatePed(pos, Gender.Male);
            if (p == null || !Exists(p)) return;
            p.Heading = dl.Heading >= 0f ? dl.Heading : (float)(rnd.NextDouble() * 360.0);
            p.BlockPermanentEvents = true;
            p.Task.AlwaysKeepTask = true;
            p.Task.StandStill(-1);
            dl.Ped = p;
            dl.Home = p.Position; dl.HasHome = true; dl.Returning = false;
            dl.IdleAt = Game.GameTime + 300;
            dl.WasBusy = false; dl.TalkAt = 0;
        }
        catch (Exception ex) { Log("Spawn dealer: " + ex.Message); }
    }

    public bool IsDealer(Ped ped)
    {
        try
        {
            if (ped == null) return false;
            foreach (Dealer dl in dealers) if (dl.Ped != null && dl.Ped.GetHashCode() == ped.GetHashCode()) return true;
            foreach (RobbedDealer rd in robbedDealers) if (rd.Ped != null && rd.Ped.GetHashCode() == ped.GetHashCode()) return true;
        }
        catch { }
        return false;
    }

    int Visits(Dealer dl) { int v; return rep.TryGetValue(dl.Name, out v) ? v : 0; }

    bool IsRegular(Dealer dl) { return Visits(dl) >= visitsForDiscount; }

    int DealerPrice(Dealer dl, int drug)
    {
        return IsRegular(dl) ? regularPrice[drug] : buyPrice[drug];
    }

    Dealer NearDealer(Ped me, bool doorOnly)
    {
        Vector3 pp = me.Position;
        foreach (Dealer dl in dealers)
        {
            if (doorOnly && !dl.Door) continue;
            if (dl.Door)
            {
                float dx = pp.X - dl.Pos.X, dy = pp.Y - dl.Pos.Y;
                if (dx * dx + dy * dy < 1.8f * 1.8f && Math.Abs(pp.Z - dl.Pos.Z) < 2.5f) return dl;
                continue;
            }
            if (dl.Ped == null || !Exists(dl.Ped) || !dl.Ped.isAlive) continue;
            if (dl.Ped.Position.DistanceTo(pp) < 3.0f) return dl;
        }
        return null;
    }

    // a police officer within CopNearDealRange of a deal: 1 star, the dealer / buyer runs,
    // and no deals until you lose the cops (every deal needs a wanted level of 0)
    int nextCopCheck;
    bool CopNearDeal(Ped me, int now)
    {
        if (copNearDealRange <= 0) return false;
        bool dealing = mode == M_DEALER || mode == M_SELL || scene == S_APPROACH || scene == S_BUY || scene == S_DEAL;
        if (!dealing || now < nextCopCheck) return false;
        nextCopCheck = now + 400;
        Ped cop = null;
        try
        {
            foreach (Ped p in World.GetPeds(me.Position, (float)copNearDealRange))
            {
                if (p == null || !Exists(p) || !p.isAliveAndWell || p.GetHashCode() == me.GetHashCode()) continue;
                if (p.RelationshipGroup == RelationshipGroup.Cop) { cop = p; break; }
            }
        }
        catch (Exception ex) { Log("CopNearDeal: " + ex.Message); return false; }
        if (cop == null) return false;

        Log("A cop saw the deal");
        Ped npc = null;
        Dealer dl = curDealer;
        if (scene == S_APPROACH) { npc = scenePed; if (approachDl != null) dl = approachDl; }
        else if (scene == S_BUY) npc = scenePed;
        else if (scene == S_DEAL) npc = scenePed;
        else if (mode == M_DEALER && dl != null) npc = dl.Ped;
        else if (mode == M_SELL) npc = curBuyer;
        if (scene != S_NONE) { try { me.Task.ClearAll(); } catch { } EndScene(); }
        CloseMenu();
        approachDl = null;
        if (dl != null && !dl.Door)
        {
            // this dealer is off his spot for the robbed-dealer time
            dl.Ped = null;
            dl.NextSpawnTry = now + 600000;
            dl.Returning = false;
        }
        Offer o;
        if (npc != null && offers.TryGetValue(npc.GetHashCode(), out o)) o.Done = true;
        if (Alive(npc)) { NoRagdoll(npc, false); Flee(npc, me); Hold(npc, 30000); }
        try { if (Player.WantedLevel < 1) Player.WantedLevel = 1; } catch { }
        return true;
    }

    // a dealer who pulled a gun on you: when he dies he leaves DealerFightCash on the ground
    class ArmedDealer { public Ped Ped; public int Since; }
    readonly List<ArmedDealer> armedDealers = new List<ArmedDealer>();
    int dealerFightCashMin = 75, dealerFightCashMax = 450;

    void AddArmedDealer(Ped p)
    {
        if (p == null) return;
        ArmedDealer a = new ArmedDealer();
        a.Ped = p; a.Since = Game.GameTime;
        armedDealers.Add(a);
    }

    void UpdateArmedDealers(int now)
    {
        for (int i = armedDealers.Count - 1; i >= 0; i--)
        {
            ArmedDealer a = armedDealers[i];
            bool exists = false;
            try { exists = a.Ped != null && Exists(a.Ped); } catch { }
            if (!exists || now - a.Since > 900000 || now < a.Since) { armedDealers.RemoveAt(i); continue; }
            bool dead = false;
            try { dead = !a.Ped.isAlive; } catch { }
            if (!dead) continue;
            armedDealers.RemoveAt(i);
            try
            {
                Vector3 pos = a.Ped.Position;
                try { pos = World.GetGroundPosition(pos); } catch { }
                Loot lt = new Loot();
                lt.Born = now; lt.Pos = pos; lt.Dealer = true;
                for (int k = 0; k < 3; k++) lt.Drugs[k] = DealerLootDrugs;
                lt.Pickup = Pickup.CreateMoneyPickup(pos, rnd.Next(dealerFightCashMin, dealerFightCashMax + 1));
                loots.Add(lt);
            }
            catch (Exception ex) { Log("Dealer cash drop: " + ex.Message); }
        }
    }

    void UpdateStock(Dealer dl)
    {
        if (!dl.StockSet)
        {
            dl.StockSet = true;
            for (int i = 0; i < 3; i++) { dl.Stock[i] = dealerStock[i]; dl.RestockAt[i] = 0; }
            return;
        }
        int tc = Environment.TickCount;
        for (int i = 0; i < 3; i++)
            if (dl.RestockAt[i] != 0 && tc - dl.RestockAt[i] >= 0) { dl.Stock[i] = dealerStock[i]; dl.RestockAt[i] = 0; }
    }

    bool CanBuy(Dealer dl, int drug, int money)
    {
        if (dl == null) return false;
        UpdateStock(dl);
        int q = buyStep[drug];
        return dl.Stock[drug] >= q && maxCarry[drug] - stash[drug] >= q && money >= DealerPrice(dl, drug);
    }

    // one press in the dealer menu: one pack goes on the order. The animations play once, when the menu closes.
    void BuyPress(int drug, Ped me, int now)
    {
        Dealer dl = curDealer;
        int money = Player.Money;
        if (!CanBuy(dl, drug, money)) return;           // greyed out: nothing happens
        int qty = buyStep[drug];
        int cost = DealerPrice(dl, drug);
        Player.Money = money - cost;
        stash[drug] += qty;
        order[drug] += qty;
        lastBoughtDrug = drug;
        dl.Stock[drug] -= qty;
        if (dl.Stock[drug] < buyStep[drug] && dl.RestockAt[drug] == 0)
        {
            int at = Environment.TickCount + restockMinutes * 60000;
            dl.RestockAt[drug] = at == 0 ? 1 : at;
        }
        if (!dl.VisitCounted)
        {
            // the first purchase of a visit counts toward regular prices
            dl.VisitCounted = true;
            rep[dl.Name] = Visits(dl) + 1;
        }
        SaveStash();
        lastBuyPress = now;
        buyCount++;
        if (buyCount % 5 == 0) PlayPlayer("BuyDuring");        // every 5th press at this dealer
    }

    int OrderTotal() { return order[0] + order[1] + order[2]; }

    // the dealer menu closes: if anything was ordered the hand-over plays once, otherwise he goes back to his spot
    void FinishOrder(Ped me, int now)
    {
        Dealer dl = curDealer;
        CloseMenu();
        if (dl == null) return;
        if (OrderTotal() > 0 && (dl.Door || Alive(dl.Ped))) StartBuy(dl, me, now);
        else { if (Alive(dl.Ped)) Say(dl.Ped, speechDealerNoSale); DealerGoHome(dl, now); }
        for (int i = 0; i < 3; i++) order[i] = 0;
    }

    void DealerGoHome(Dealer dl, int now)
    {
        if (dl == null || dl.Door || !Alive(dl.Ped) || !dl.HasHome) return;
        try
        {
            if (dl.Ped.Position.DistanceTo(dl.Home) > 0.8f)
            {
                dl.Ped.Task.ClearAll();
                GTA.Native.Function.Call("TASK_GO_STRAIGHT_TO_COORD", dl.Ped, dl.Home.X, dl.Home.Y, dl.Home.Z, 2, 15000);
                dl.Returning = true;
                dl.ReturnSince = now;
            }
        }
        catch (Exception ex) { Log("DealerGoHome: " + ex.Message); }
        dl.WasBusy = true;
    }

    // ------------------------------------------------------------------ walking up, face to face
    static float HeadingTo(Vector3 from, Vector3 to)
    {
        double h = Math.Atan2(-(to.X - from.X), to.Y - from.Y) * 180.0 / Math.PI;
        if (h < 0) h += 360.0;
        return (float)h;
    }

    // the spot dealDistance in front of the player, on the line towards the NPC
    Vector3 MeetSpot(Ped npc, Ped me)
    {
        Vector3 pp = me.Position, np = npc.Position;
        float dx = np.X - pp.X, dy = np.Y - pp.Y;
        float len = (float)Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.05f) return np;
        return new Vector3(pp.X + dx / len * dealDistance, pp.Y + dy / len * dealDistance, np.Z);
    }

    float Dist2D(Ped a, Ped b)
    {
        Vector3 x = a.Position, y = b.Position;
        float dx = x.X - y.X, dy = x.Y - y.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    void WalkUp(Ped npc, Ped me, int now)
    {
        try
        {
            approachTarget = MeetSpot(npc, me);
            GTA.Native.Function.Call("TASK_GO_STRAIGHT_TO_COORD", npc, approachTarget.X, approachTarget.Y, approachTarget.Z, 2, 10000);
        }
        catch (Exception ex) { Log("WalkUp: " + ex.Message); }
        approachIssued = now;
    }

    // snap both to face each other: the trade animations are made for two people standing square on
    void FaceEachOther(Ped npc, Ped me)
    {
        try
        {
            npc.Heading = HeadingTo(npc.Position, me.Position);
            me.Heading = HeadingTo(me.Position, npc.Position);
        }
        catch { }
    }

    // a dealer (dl != null) or a buyer walks up to the player; the menu opens when he is there
    void StartApproach(Ped npc, Dealer dl, string title, Ped me, int now)
    {
        BeginScene(S_APPROACH, npc, now);
        approachDl = dl;
        approachTitle = title;
        if (dl != null) { buyCount = 0; PlayPlayer("BuyBegin"); dl.LastBuyBegin = now; }
        else { LaterChance(0, "SellBegin", sellBeginChance); Say(npc, speechBuyerAgree); }
        if (dl == null) Grab(npc);
        else { dl.WasBusy = true; dl.Returning = false; NoRagdoll(npc, true); }
        approachStartDist = Dist2D(npc, me);
        // a buyer just stops and turns to you (you walk up to him); a dealer comes over only from a distance
        if (dl != null && approachStartDist > 2.2f)
        {
            try { npc.Task.ClearAll(); } catch { }
            WalkUp(npc, me, now);
        }
        else
        {
            try { npc.Task.ClearAll(); npc.Task.TurnTo(me); } catch { }
            NextPhase(1, now);
        }
        if (dl != null && (dl.LastGreet == 0 || now - dl.LastGreet > 30000 || now < dl.LastGreet))
        {
            dl.LastGreet = now;
            Say(npc, speechDealerGreet);
        }
    }

    float approachStartDist;

    void TickApproach(Ped me, int now)
    {
        Ped npc = scenePed;
        Dealer dl = approachDl;
        if (!Alive(npc) || me.isInVehicle() || Player.WantedLevel > 0)
        {
            if (dl == null && Alive(npc)) Release(npc);
            EndScene();
            if (dl != null) { NoRagdoll(npc, false); DealerGoHome(dl, now); }
            return;
        }
        int t = now - phaseStart;
        float d = Dist2D(npc, me);
        if (scenePhase == 0)
        {
            // there, or the walk isn't getting anywhere: the menu opens either way (never wait on a stuck walk)
            bool there = d <= dealDistance + 0.4f
                || (t > 1500 && approachStartDist - d < 0.3f)
                || t > 6000;
            if (!there)
            {
                // the player moved: walk to the new spot
                if (now - approachIssued > 700)
                {
                    Vector3 spot = MeetSpot(npc, me);
                    if (spot.DistanceTo(approachTarget) > 0.4f) { approachStartDist = d; WalkUp(npc, me, now); phaseStart = now; }
                }
                return;
            }
            try { npc.Task.ClearAll(); npc.Task.TurnTo(me); } catch { }
        }
        // the menu opens
        string title = approachTitle;
        EndScene();
        approachDl = null;
        if (dl != null)
        {
            for (int i = 0; i < 3; i++) order[i] = 0;
            OpenDealerMenu(dl, me, now, !OwnBuyerUi() || aimHeld);
        }
        else
        {
            OpenSellNow(npc, now);
            if (mode == M_SELL) { if (title != null && title.Trim().Length > 0) sellTitle = title.Trim().ToUpper(); }
            else { Release(npc); }
        }
    }

    // right before the trade: stand him exactly DealDistance in front of the player, square on.
    // A small gap is closed on the spot; returns false if he is too far and has to walk first.
    bool PlaceForTrade(Ped npc, Ped me)
    {
        try
        {
            float d = Dist2D(npc, me);
            if (d > dealDistance + 0.9f) return false;
            Vector3 pp = me.Position, np = npc.Position;
            float dx = np.X - pp.X, dy = np.Y - pp.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.05f) { float h = me.Heading * (float)Math.PI / 180f; dx = -(float)Math.Sin(h); dy = (float)Math.Cos(h); len = 1f; }
            float x = pp.X + dx / len * dealDistance, y = pp.Y + dy / len * dealDistance;
            GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", npc, x, y, np.Z);
        }
        catch (Exception ex) { Log("PlaceForTrade: " + ex.Message); }
        FaceEachOther(npc, me);
        return true;
    }

    float AnimPhase(Ped p, string[] a)
    {
        try
        {
            AnimationSet aset;
            if (a == null || a.Length < 2 || !animSets.TryGetValue(a[0], out aset)) return -1f;
            return p.Animation.GetCurrentAnimationTime(aset, a[1]);
        }
        catch { return -1f; }
    }

    // the receiver's hand has reached the giver's: the drugs change hands
    bool HandoverNow(Ped receiver, string[] a, int now)
    {
        int t = now - tradeStartAt;
        float ph = AnimPhase(receiver, a);
        if (ph > 0.01f && ph <= 1.01f && ph >= handoverPhase) return true;
        return t >= tradeHandoverMs;
    }

    // take_obj has played out (arms down): the trade is over
    bool TradeDone(Ped receiver, string[] a, int now)
    {
        int t = now - tradeStartAt;
        if (t >= tradeEndMs) return true;
        if (t > 500 && !AnimPlaying(receiver, a)) return true;
        float ph = AnimPhase(receiver, a);
        return ph >= 0.97f && ph <= 1.01f;
    }

    // the trade is over: both stop on the spot, no tail of the animation
    void StopTrade(Ped p)
    {
        try { if (p != null && Exists(p)) p.Task.ClearAllImmediately(); } catch { }
    }


    // LB at a dealer's menu. He either gives up and drops his money and some product, or fights.
    // Either way the regular-customer progress with him is gone.
    void RobDealer(Dealer dl, Ped me, int now)
    {
        Ped p = dl.Ped;
        rep[dl.Name] = 0;
        dl.VisitCounted = true;
        SaveStash();
        dl.Ped = null;                         // out of business at this spot for a while
        dl.NextSpawnTry = now + 600000;
        if (rnd.Next(100) < dealerRobComply)
        {
            BeginScene(S_DEALROB, p, now);
            try { p.Task.ClearAll(); p.Task.HandsUp(2500); } catch { }
            Say(p, speechDealerRobbed);
        }
        else
        {
            CloseMenu();
            GiveWeapon(p, dealerFightWeapon);
            try { p.Task.ClearAll(); p.Task.FightAgainst(me); } catch { }
            Say(p, speechDealerFight);
            AddArmedDealer(p);
            Hold(p, 60000);
        }
    }

    void TickDealRob(Ped me, int now)
    {
        Ped p = scenePed;
        if (!Alive(p)) { EndScene(); return; }
        int t = now - phaseStart;
        switch (scenePhase)
        {
            case 0:     // hands up
                if (t >= 1500) { Anim(p, animDealerDropCash); NextPhase(1, now); }
                break;
            case 1:     // drops it
                if (t >= 1000) { DropDealerStash(p, now); NextPhase(2, now); }
                break;
            case 2:     // runs
                if (t >= 700)
                {
                    Flee(p, me);
                    Hold(p, 30000);
                    EndScene();
                }
                break;
        }
    }

    // always cash; always at least one drug, sometimes two or all three
    void DropDealerStash(Ped p, int now)
    {
        try
        {
            Loot lt = new Loot();
            lt.Born = now;
            Vector3 pos = p.GetOffsetPosition(new Vector3(0f, 0.8f, 0f));
            try { pos = World.GetGroundPosition(pos); } catch { }
            lt.Pos = pos;
            lt.Dealer = true;
            for (int i = 0; i < 3; i++) lt.Drugs[i] = DealerLootDrugs;
            int cash = rnd.Next(dealerRobCashMin, dealerRobCashMax + 1);
            lt.Pickup = Pickup.CreateMoneyPickup(pos, Math.Max(1, cash));
            loots.Add(lt);
        }
        catch (Exception ex) { Log("DropDealerStash: " + ex.Message); }
    }


    // ------------------------------------------------------------------ scene: buying from the dealer
    // one hand-over for the whole order: wallet out, then buy_drugs / sell_drugs on the same frame
    void StartBuy(Dealer dl, Ped me, int now)
    {
        // the prop is the last thing bought (coke pack once that order reaches CokePackFrom)
        int drug = lastBoughtDrug >= 0 && order[lastBoughtDrug] > 0 ? lastBoughtDrug : 0;
        if (order[drug] <= 0) for (int i = 0; i < 3; i++) if (order[i] > 0) drug = i;
        int qty = order[drug];
        BeginScene(S_BUY, dl.Ped, now);
        curDealer = dl;
        sceneDoor = dl.Door || dl.Ped == null;
        sceneDrug = drug; sceneQty = qty;
        DropProp(ref sceneMoney);
        LockPlayer(true);
        try
        {
            me.Task.ClearAll();
            if (!sceneDoor) { dl.Ped.Task.ClearAll(); NoRagdoll(dl.Ped, true); PlaceForTrade(dl.Ped, me); }
        }
        catch { }
        // together: you take your cash out of your pocket, he takes the drugs out of his jacket
        Anim(me, animWallet);
        if (!sceneDoor) Anim(dl.Ped, animWalletChest);
        putMeDone = false; putNpcDone = sceneDoor;
    }

    // Buying: m_getoutwallet_pocket (the cash is in your hand from frame 28) plays to its end, hands at the chest;
    // the hand-over (you take_obj, the dealer give_obj): your cash goes to him, his drugs come to you; then you
    // put the drugs away (m_putwalletaway_chest, gone at frame 22) while he pockets the cash
    // (m_putwalletaway_pocket, gone at frame 33)
    const int BuyCashFrame = 28, BuyDrugsGoneFrame = 22, DealerCashGoneFrame = 33, DealerDrugsFrame = 38;

    void TickBuy(Ped me, int now)
    {
        Ped dp = scenePed;
        if ((!sceneDoor && !Alive(dp)) || me.isInVehicle()) { EndBuy(me, now); return; }
        int t = now - phaseStart;
        switch (scenePhase)
        {
            case 0:     // your cash out of your pocket (in your hand from frame 28) and, at the same time, his drugs out
                        // of his jacket (m_getoutwallet_chest, in his hand from frame 38). Whoever is ready first is
                        // held there, hands at the chest, until the other one is.
                {
                    if (sceneMoney == null && AnimFrame(me, animWallet, phaseStart, now) >= BuyCashFrame) sceneMoney = MakeMoney(me);
                    if (!sceneDoor && sceneProp == null && AnimFrame(dp, animWalletChest, phaseStart, now) >= DealerDrugsFrame) sceneProp = MakeProp(sceneDrug, sceneQty, dp);
                    float ph = AnimPhase(me, animWallet);
                    if (!putMeDone && ((ph >= 0.95f && ph <= 1.01f) || (t > 600 && !AnimPlaying(me, animWallet)))) { putMeDone = true; AnimSpeedOf(me, animWallet, 0f); }
                    if (!putNpcDone)
                    {
                        float pd = AnimPhase(dp, animWalletChest);
                        if ((pd >= 0.95f && pd <= 1.01f) || (t > 800 && !AnimPlaying(dp, animWalletChest))) { putNpcDone = true; AnimSpeedOf(dp, animWalletChest, 0f); }
                    }
                    if ((putMeDone && putNpcDone) || t > walletMs + 2500)
                    {
                        if (sceneMoney == null) sceneMoney = MakeMoney(me);
                        if (!sceneDoor)
                        {
                            PlaceForTrade(dp, me);
                            if (sceneProp == null) sceneProp = MakeProp(sceneDrug, sceneQty, dp);
                            Anim(dp, animDealerSell);
                        }
                        Anim(me, animPlayerBuy);
                        tradeStartAt = now;
                        NextPhase(1, now);
                    }
                }
                break;
            case 1:     // the hands meet: cash to him, drugs to you
                if (HandoverNow(me, animPlayerBuy, now))
                {
                    if (sceneDoor) { DropProp(ref sceneMoney); sceneProp = MakeProp(sceneDrug, sceneQty, me); }
                    else { HandProp(sceneProp, me); HandMoney(sceneMoney, dp); }
                    NextPhase(2, now);
                }
                break;
            case 2:     // both finish together; then each puts away what he got
                if (TradeDone(me, animPlayerBuy, now))
                {
                    Anim(me, animPutAway);
                    putMeDone = false; putNpcDone = sceneDoor || !Alive(dp);
                    if (!putNpcDone) Anim(dp, animPutAwayPocket);
                    NextPhase(3, now);
                }
                break;
            case 3:     // the drugs into your jacket, the cash into his pocket
                {
                    if (sceneProp != null && AnimFrame(me, animPutAway, phaseStart, now) >= BuyDrugsGoneFrame) DropProp(ref sceneProp);
                    if (!putNpcDone && sceneMoney != null && Alive(dp) && AnimFrame(dp, animPutAwayPocket, phaseStart, now) >= DealerCashGoneFrame) DropProp(ref sceneMoney);
                    if (!putMeDone && AnimDone(me, animPutAway, t, 4500)) putMeDone = true;
                    if (!putNpcDone && (!Alive(dp) || AnimDone(dp, animPutAwayPocket, t, 4500))) putNpcDone = true;
                    if (putMeDone && putNpcDone) { DropProp(ref sceneProp); DropProp(ref sceneMoney); EndBuy(me, now); }
                }
                break;
        }
    }

    void EndBuy(Ped me, int now)
    {
        Dealer dl = curDealer;
        Ped dp = scenePed;
        StopTrade(me);
        if (!sceneDoor && Alive(dp)) { StopTrade(dp); NoRagdoll(dp, false); }
        EndScene();
        curDealer = null;
        if (!sceneDoor && Alive(dp)) Say(dp, speechDealerDone);
        if (dl != null) DealerGoHome(dl, now);
        Later(2000, "BuyAfter");             // waits for the dealer to finish his line
    }

    // ------------------------------------------------------------------ buyers
    void BuildModelKinds()
    {
        modelKinds = new Dictionary<int, int>();
        AddKinds(businessModels, KIND_BUSINESS);
        AddKinds(hookerModels, KIND_HOOKER);
        AddKinds(bumModels, KIND_BUM);
        AddKinds(gangModels, KIND_GANG);
    }

    void AddKinds(string[] names, int kind)
    {
        foreach (string n in names)
        {
            try { modelKinds[ModelHash(n)] = kind; } catch { }
        }
    }

    int BuyerKind(Ped ped)
    {
        if (modelKinds == null) BuildModelKinds();
        int kind;
        if (modelKinds.TryGetValue(ped.Model.Hash, out kind)) return kind;
        switch (ped.RelationshipGroup)
        {
            case RelationshipGroup.Prostitute: return KIND_HOOKER;
            case RelationshipGroup.Bum: return KIND_BUM;
            case RelationshipGroup.Gang_Albanian:
            case RelationshipGroup.Gang_Biker1:
            case RelationshipGroup.Gang_Biker2:
            case RelationshipGroup.Gang_Italian:
            case RelationshipGroup.Gang_Russian1:
            case RelationshipGroup.Gang_Russian2:
            case RelationshipGroup.Gang_Irish:
            case RelationshipGroup.Gang_Jamaican:
            case RelationshipGroup.Gang_AfricanAmerican:
            case RelationshipGroup.Gang_Korean:
            case RelationshipGroup.Gang_ChineseJapanese:
            case RelationshipGroup.Gang_PuertoRican:
                return KIND_GANG;
        }
        // the game's own ped type: hookers and bums that are not on the model lists
        try
        {
            string t = ped.PedType.ToString().Replace("_", "").ToUpperInvariant();
            if (t.Contains("PROSTITUTE") || t.Contains("HOOKER")) return KIND_HOOKER;
            if (t.Contains("BUM")) return KIND_BUM;
        }
        catch { }
        try
        {
            int mh = ped.Model.Hash;
            if (notBuyerLogged.Count < 25 && !notBuyerLogged.ContainsKey(mh))
            {
                notBuyerLogged[mh] = true;
                Log("Not a buyer: model hash " + ped.Model.Hash + ", ped type " + ped.PedType + ", group " + ped.RelationshipGroup);
            }
        }
        catch { }
        return KIND_NONE;
    }
    Dictionary<int, bool> notBuyerLogged = new Dictionary<int, bool>();   // one log line per model that is not a buyer

    bool IsThief(Ped ped)
    {
        foreach (Thief t in thieves) if (t.Ped != null && t.Ped.GetHashCode() == ped.GetHashCode()) return true;
        return false;
    }

    // true = this NPC can be sold to (businessmen, hookers, bums, gangsters)
    public bool IsBuyer(Ped ped)
    {
        try
        {
            if (!inited || ped == null || !Exists(ped) || !ped.isAlive) return false;
            if (ped.GetHashCode() == Player.Character.GetHashCode() || ped.isInVehicle()) return false;
            if (IsDealer(ped) || IsThief(ped)) return false;
            Offer o;
            if (offers.TryGetValue(ped.GetHashCode(), out o)) return !o.Done;
            return BuyerKind(ped) != KIND_NONE;
        }
        catch { return false; }
    }

    Offer GetOffer(Ped ped)
    {
        Offer o;
        if (offers.TryGetValue(ped.GetHashCode(), out o)) return o;
        o = new Offer();
        o.Kind = BuyerKind(ped);
        if (o.Kind == KIND_NONE) o.Kind = KIND_OTHER;
        o.Born = Game.GameTime;
        o.Bulk = rnd.Next(100) < bulkBuyerChance;
        // he wants one, two or all three drugs; a bulk buyer wants BulkAmount of one
        bool[] want = new bool[3];
        if (o.Bulk) want[rnd.Next(3)] = true;
        else
        {
            int n = 0;
            for (int i = 0; i < 3; i++) { want[i] = rnd.Next(100) < 45; if (want[i]) n++; }
            if (n == 0) want[rnd.Next(3)] = true;
        }
        for (int i = 0; i < 3; i++)
        {
            o.Qty[i] = !want[i] ? 0 : (o.Bulk ? bulkAmount : WantAmount(i));
            o.Price[i] = sellPrice[i];
        }
        offers[ped.GetHashCode()] = o;
        return o;
    }

    // mostly small amounts, sometimes a lot (7+ is a risky buyer)
    int WantAmount(int drug)
    {
        int lo = sellMin[drug], hi = Math.Max(sellMin[drug], sellMax[drug]);
        int r = rnd.Next(100), q;
        if (r < 55) q = rnd.Next(1, 4);
        else if (r < 85) q = rnd.Next(4, 7);
        else q = rnd.Next(7, 10);
        return Math.Max(lo, Math.Min(hi, q));
    }

    // a bulk buyer takes BulkAmount or nothing; a normal buyer takes what he wants or what you have
    int SellQty(Offer o, int drug)
    {
        if (o.Bulk) return o.Qty[drug] > 0 && stash[drug] >= bulkAmount ? bulkAmount : 0;
        return Math.Min(o.Qty[drug], stash[drug]);
    }

    int SellCash(Offer o, int drug, int qty)
    {
        return o.Bulk ? bulkSellPrice[drug] : qty * sellPrice[drug];
    }

    string NoSale(Offer o, int drug)
    {
        if (o.Bulk && stash[drug] > 0) return "He only takes " + Amount(drug, bulkAmount) + " at once.";
        return "You have no " + DrugName[drug].ToLower() + " to sell.";
    }

    // Bridge: the dialogue script calls this when the player picks "Sell Drugs".
    public void OpenSell(Ped ped)
    {
        OpenSellAs(ped, null);
    }

    // GreetTauntRob: Buy Drugs (D-pad right) while locked on to a dealer
    public void OpenDealerRequest(Ped ped) { pendingOpenDealer = ped; SwallowAttack(); }

    // RT is also the attack button: cancel the swing it would start and hold the controls a moment
    int controlBackAt;
    bool rtPrev;
    void SwallowAttack()
    {
        try
        {
            Ped me = Player.Character;
            if (me == null || !Exists(me) || me.isInVehicle()) return;
            // never while you are getting into (or pulling someone out of) a car
            try { if (GTA.Native.Function.Call<bool>("IS_CHAR_GETTING_IN_TO_A_CAR", me)) return; } catch { }
            me.Task.ClearAllImmediately();
            if (!playerLocked) { Player.CanControlCharacter = false; controlBackAt = Game.GameTime + 350; }
        }
        catch { }
    }

    // GreetTauntRob: LB at gunpoint on a dealer. GreetTauntRob plays the hands-up and wallet animations.
    public void RobDealerStartRequest(Ped ped) { pendingRobStart = ped; pendingRobFight = false; }

    // DynamicScenarios: a stolen drug shipment. Handled on Pusher's own update (below), never on the caller's thread.
    public bool GiveMaxDrugsRequest() { pendingGiveMax = true; return true; }

    // GreetTauntRob: LB at gunpoint. true = he gives up (GreetTauntRob plays hands up + the drop), false = he pulls a gun
    public bool RobDealerRequest(Ped ped)
    {
        bool comply = rnd.Next(100) < dealerRobComply;
        pendingRobStart = ped;
        pendingRobFight = !comply;
        return comply;
    }

    // GreetTauntRob: the moment the robbed dealer lets go of the money
    public void DealerDropRequest(Ped ped) { if (ped != null) pendingDrops.Add(ped); }

    Dealer DealerOf(Ped p)
    {
        if (p == null) return null;
        foreach (Dealer dl in dealers) if (dl.Ped != null && dl.Ped == p) return dl;
        return null;
    }

    void OpenDealerMenu(Dealer dl, Ped me, int now, bool lockOn)
    {
        CloseMenu();
        mode = M_DEALER; curDealer = dl; menuOpened = now;
        dealerLockOn = lockOn;
        ltReleasedAt = 0;
        try { if (dl.Ped != null) dl.Ped.Task.TurnTo(me); } catch { }
        if (dl.LastGreet == 0 || now - dl.LastGreet > 30000 || now < dl.LastGreet)
        {
            dl.LastGreet = now;
            if (dl.Ped != null) dl.TalkAt = now + 900;
            Say(dl.Ped, speechDealerGreet);
        }
    }

    // Same, but the menu is titled with the caller's own name for this NPC (a faction name).
    public void OpenSellAs(Ped ped, string title)
    {
        if (!dialogueCompat) return;
        pendingSellTitle = title;
        pendingSell = ped;
        SwallowAttack();
    }

    void OpenSellNow(Ped ped, int now)
    {
        if (IsDealer(ped) || IsThief(ped)) return;
        Offer o = GetOffer(ped);
        if (o.Done) return;
        curBuyer = ped;
        curOffer = o;
        curDealer = null;
        sellTitle = o.Bulk ? "BULK BUYER" : "BUYER";
        mode = M_SELL;
        menuOpened = now;
        lastSellPress = now;
        ltWasHeld = aimHeld;
        ltReleasedAt = 0;
    }

    // the dialogue script has its Pusher section: it owns the lock-on UI
    bool OwnBuyerUi()
    {
        if (!dialogueCompat) return true;
        try
        {
            AppDomain d = AppDomain.CurrentDomain;
            if (!true.Equals(d.GetData("GreetTauntRob.PusherCompat"))) return true;   // not installed, or an old version without the Pusher section
            object a = d.GetData("GreetTauntRob.Alive");
            if (!(a is int)) return true;
            int age = Game.GameTime - (int)a;
            return !(age > -3000 && age < 3000);                                       // installed but not running: Pusher draws its own menus
        }
        catch { return true; }
    }

    Ped FindTarget(Ped me)
    {
        try
        {
            foreach (Ped p in World.GetPeds(me.Position, 14f))
            {
                if (p == null || !Exists(p) || p.GetHashCode() == me.GetHashCode() || !p.isAlive) continue;
                if (GTA.Native.Function.Call<bool>("IS_PLAYER_TARGETTING_CHAR", Player, p)) return p;
                if (GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_AIMING_AT_CHAR", Player, p)) return p;
            }
        }
        catch (Exception ex) { Log("FindTarget: " + ex.Message); }
        return null;
    }

    bool Unarmed(Ped me)
    {
        try { return me.Weapons.CurrentType == Weapon.Unarmed; } catch { return true; }
    }

    // ------------------------------------------------------------------ menus
    void TickMenus(Ped me, int now)
    {
        bool onFoot = !me.isInVehicle();
        int wanted = Player.WantedLevel;

        // buying and selling happen on foot only
        if (!onFoot)
        {
            if (mode != M_NONE) CloseMenu();
            return;
        }

        bool gtr = !OwnBuyerUi();
        if (mode == M_DEALER && dealerLockOn)
        {
            // opened from GreetTauntRob's Buy Drugs: stays up while LT is held
            Dealer dl = curDealer;
            bool ok = dl != null && Alive(dl.Ped) && wanted == 0 && dl.Ped.Position.DistanceTo(me.Position) < 8f;
            if (ok)
            {
                if (aimHeld) ltReleasedAt = 0;
                else if (ltReleasedAt == 0) ltReleasedAt = now;
                else if (now - ltReleasedAt > 400) ok = false;
            }
            if (!ok) { FinishOrder(me, now); return; }
            if (now - menuOpened >= 350)
                for (int i = 0; i < 3; i++)
                    if (btn[B_D0 + i].Pressed) { BuyPress(i, me, now); break; }
            return;
        }

        // walk-up menu: door dealers always; dealers with a ped only when GreetTauntRob is not there
        Dealer near = (onFoot && wanted == 0) ? NearDealer(me, gtr) : null;
        if (near != null)
        {
            if (mode != M_DEALER || curDealer != near)
            {
                for (int i = 0; i < 3; i++) order[i] = 0;
                OpenDealerMenu(near, me, now, false);
                // walking up to him counts as starting a visit (not again right after a hand-over)
                if (near.LastBuyBegin == 0 || now - near.LastBuyBegin > 20000 || now < near.LastBuyBegin)
                {
                    buyCount = 0;
                    PlayPlayer("BuyBegin");
                }
                near.LastBuyBegin = now;
            }
            if (now - menuOpened >= 300)
            {
                if (!gtr && OrderTotal() == 0 && btn[B_ROB].Pressed && !near.Door && Alive(near.Ped)) { RobDealer(near, me, now); return; }
                for (int i = 0; i < 3; i++)
                    if (btn[B_D0 + i].Pressed && near.Sells(i)) { BuyPress(i, me, now); break; }
                // walk-up menu: the hand-over plays once you start to walk off or stop ordering
                if (OrderTotal() > 0 && (stickMoved || now - lastBuyPress > 2500)) { FinishOrder(me, now); return; }
            }
            return;
        }
        if (mode == M_DEALER) { if (OrderTotal() > 0) FinishOrder(me, now); else CloseMenu(); }

        if (mode == M_SELL) { TickSellMenu(me, now); return; }

        if (!OwnBuyerUi() || !onFoot || wanted > 0 || !aimHeld || (sellOnlyUnarmed && !Unarmed(me)))
        {
            if (mode == M_BUYER) CloseMenu();
            scanTarget = null;
            return;
        }
        if (now - lastTargetScan >= 90 || now < lastTargetScan)
        {
            lastTargetScan = now;
            scanTarget = FindTarget(me);
        }
        if (scanTarget != null && IsBuyer(scanTarget))
        {
            mode = M_BUYER; curBuyer = scanTarget;
            if (btn[B_SELL].Pressed && !GetOffer(scanTarget).Done) { SwallowAttack(); StartApproach(scanTarget, null, null, me, now); }
        }
        else if (mode == M_BUYER) CloseMenu();
    }

    void TickSellMenu(Ped me, int now)
    {
        if (!Alive(curBuyer) || curOffer == null || me.isInVehicle() || Player.WantedLevel > 0
            || curBuyer.Position.DistanceTo(me.Position) > 9f)
        {
            EndSellMenu(me, now); return;
        }
        bool leave = now - lastSellPress > 15000;
        // opened while locked on: letting go of the lock-on closes it
        if (ltWasHeld)
        {
            if (aimHeld) ltReleasedAt = 0;
            else if (ltReleasedAt == 0) ltReleasedAt = now;
            else if (now - ltReleasedAt > 400) leave = true;
        }
        if (leave) { EndSellMenu(me, now); return; }
        if (now - menuOpened < 350) return;
        for (int i = 0; i < 3; i++)
            if (btn[B_D0 + i].Pressed) { SellPress(i, me, now); break; }
    }

    // the menu closes: if anything was sold, the hand-over happens and he walks off
    void EndSellMenu(Ped me, int now)
    {
        Offer o = curOffer;
        Ped b = curBuyer;
        if (o != null && o.Units > 0 && Alive(b)) { FinishSale(me, now); return; }
        CloseMenu();
        if (Alive(b)) Say(b, speechBuyerNoSale);
        if (Alive(b)) { try { b.Task.ClearAll(); b.Task.WanderAround(); } catch { } Release(b); }
    }

    void FinishSale(Ped me, int now)
    {
        Offer o = curOffer;
        Ped b = curBuyer;
        o.Done = true;
        int drug = 0;
        for (int i = 1; i < 3; i++) if (o.Sold[i] > o.Sold[drug]) drug = i;
        sceneDrug = drug; sceneQty = Math.Max(1, o.Sold[drug]); sceneCash = 0;
        CloseMenu();
        StartDeal(b, me, now, o);
        scenePaid = true;                       // the money was paid press by press
    }

    // One press: one gram / pill (a bulk buyer takes BulkAmount once). Trouble is rolled on the first press.
    void SellPress(int drug, Ped me, int now)
    {
        Offer o = curOffer;
        Ped ped = curBuyer;
        int left = o.Qty[drug] - o.Sold[drug];
        int qty;
        if (o.Bulk) qty = (o.Units == 0 && o.Qty[drug] > 0 && stash[drug] >= bulkAmount) ? bulkAmount : 0;
        else qty = (left > 0 && stash[drug] > 0) ? 1 : 0;
        if (qty <= 0) return;                   // greyed out: nothing happens
        lastSellPress = now;

        if (!o.Risky)
        {
            o.Risky = true;
            if (o.Kind == KIND_HOOKER && rnd.Next(100) < hookerStingChance) { o.Done = true; CloseMenu(); StartSting(ped, me, now); return; }
            // one roll out of 100: cash robbery, drug robbery, or a clean sale (bulk = 10 or more at once)
            int units = 0;
            for (int i = 0; i < 3; i++) units += o.Qty[i];
            bool bulkSale = o.Bulk || units >= bulkRobFrom;
            int cashP = bulkSale ? bulkRobChance : robChance, drugP = bulkSale ? bulkPunchChance : punchChance;
            int roll = rnd.Next(100);
            bool hooker = o.Kind == KIND_HOOKER;     // a hooker never robs you: she buys, or she is undercover
            bool cashRob = !hooker && roll < cashP;
            bool drugRob = !hooker && !cashRob && roll < cashP + drugP;
            if (cashRob) { o.Done = true; StartRob(ped, me, now); return; }
            if (drugRob)
            {
                o.Done = true;
                sceneDrug = drug;
                sceneQty = Math.Max(1, Math.Min(o.Bulk ? bulkAmount : o.Qty[drug], stash[drug]));
                StartPunch(ped, me, now, false);
                return;
            }
        }

        int cash = o.Bulk ? bulkSellPrice[drug] : sellPrice[drug] * qty;
        stash[drug] -= qty;
        Player.Money = Player.Money + cash;     // straight into the player's cash
        o.Sold[drug] += qty; o.Units += qty; o.Paid += cash;
        SaveStash();

        bool more = false;
        if (!o.Bulk)
            for (int i = 0; i < 3; i++) if (o.Qty[i] - o.Sold[i] > 0 && stash[i] > 0) more = true;
        if (!more) FinishSale(me, now);
    }

    // ------------------------------------------------------------------ scene: clean deal
    // Selling: m_getoutwallet_chest (drugs in your hand from frame 36), the hand-over (you give_obj, he take_obj):
    // drugs to him, his cash to you; then you pocket the cash (m_putwalletaway_pocket, gone at frame 34) while he
    // puts the drugs in his jacket (m_putwalletaway_chest, gone at frame 27)
    const int SellDrugsFrame = 36, SellCashGoneFrame = 34, BuyerDrugsGoneFrame = 27;

    void StartDeal(Ped ped, Ped me, int now, Offer offer)
    {
        BeginScene(S_DEAL, ped, now);
        sceneOffer = offer;
        scenePaid = false;
        try
        {
            ped.Task.ClearAll();
            if (Dist2D(ped, me) > dealDistance + 0.9f) WalkUp(ped, me, now);
            else NextPhase(1, now);
        }
        catch { }
    }

    void TickDeal(Ped me, int now)
    {
        Ped ped = scenePed;
        if (!Alive(ped) || me.isInVehicle())
        {
            if (!scenePaid && sceneOffer != null) sceneOffer.Done = false;
            if (Alive(ped)) Release(ped);
            EndScene();
            return;
        }
        int t = now - phaseStart;
        switch (scenePhase)
        {
            case 0:     // not quite there yet: he steps up
                if (Dist2D(ped, me) <= dealDistance + 0.6f || t > 5000)
                {
                    if (Dist2D(ped, me) > dealDistance + 0.9f) { Release(ped); EndScene(); return; }
                    NextPhase(1, now);
                }
                break;
            case 1:     // square on
                try { ped.Task.ClearAll(); me.Task.ClearAll(); } catch { }
                PlaceForTrade(ped, me);
                LockPlayer(true);
                NextPhase(2, now);
                break;
            case 2:     // at the same time: you take the drugs out of your jacket (m_getoutwallet_chest, in your hand
                        // from frame 36) and he takes his cash out of his pocket (m_getoutwallet_pocket, in his hand
                        // from frame 28, the same as yours when you buy)
                if (t >= 100)
                {
                    PlaceForTrade(ped, me);
                    DropProp(ref sceneProp); DropProp(ref sceneMoney);
                    Anim(me, animWalletChest);
                    Anim(ped, animWallet);
                    putMeDone = false; putNpcDone = false;
                    NextPhase(6, now);
                }
                break;
            case 6:
                {
                    if (sceneProp == null && AnimFrame(me, animWalletChest, phaseStart, now) >= SellDrugsFrame) sceneProp = MakeProp(sceneDrug, sceneQty, me);
                    if (sceneMoney == null && AnimFrame(ped, animWallet, phaseStart, now) >= BuyCashFrame) sceneMoney = MakeMoney(ped);
                    // each one is ready once his hands are up at his chest (his animation at its end)
                    float ph = AnimPhase(me, animWalletChest);
                    // whoever is ready first is held there (hands at the chest) until the other one is
                    if (!putMeDone && ((ph >= 0.95f && ph <= 1.01f) || (t > 800 && !AnimPlaying(me, animWalletChest)))) { putMeDone = true; AnimSpeedOf(me, animWalletChest, 0f); }
                    float pn = AnimPhase(ped, animWallet);
                    if (!putNpcDone && ((pn >= 0.95f && pn <= 1.01f) || (t > 800 && !AnimPlaying(ped, animWallet)))) { putNpcDone = true; AnimSpeedOf(ped, animWallet, 0f); }
                    if ((putMeDone && putNpcDone) || t > walletMs + 2500)
                    {
                        if (sceneProp == null) sceneProp = MakeProp(sceneDrug, sceneQty, me);
                        if (sceneMoney == null) sceneMoney = MakeMoney(ped);
                        PlaceForTrade(ped, me);
                        Anim(me, animPlayerSell);
                        Anim(ped, animNpcBuy);
                        tradeStartAt = now;
                        PlaySellLine();
                        Say(ped, speechBuyerDone);       // after the player's Sell line
                        NextPhase(3, now);
                    }
                }
                break;
            case 3:     // the hands meet: drugs to him, cash to you
                if (HandoverNow(ped, animNpcBuy, now))
                {
                    HandProp(sceneProp, ped);
                    HandMoney(sceneMoney, me);
                    NextPhase(4, now);
                }
                break;
            case 4:     // both finish together; you pocket the cash, he puts the drugs in his jacket
                if (TradeDone(ped, animNpcBuy, now))
                {
                    Anim(me, animPutAwayPocket);
                    Anim(ped, animPutAway);
                    putMeDone = false; putNpcDone = false;
                    NextPhase(5, now);
                }
                break;
            case 5:     // cash gone at frame 34 of yours, drugs gone at frame 27 of his; then he walks off
                {
                    if (sceneMoney != null && AnimFrame(me, animPutAwayPocket, phaseStart, now) >= SellCashGoneFrame) DropProp(ref sceneMoney);
                    if (sceneProp != null && AnimFrame(ped, animPutAway, phaseStart, now) >= BuyerDrugsGoneFrame) DropProp(ref sceneProp);
                    if (!putMeDone && AnimDone(me, animPutAwayPocket, t, 4500)) { putMeDone = true; DropProp(ref sceneMoney); if (playerLocked) LockPlayer(false); }
                    if (!putNpcDone && AnimDone(ped, animPutAway, t, 4500)) putNpcDone = true;
                    if (putMeDone && putNpcDone)
                    {
                        StopTrade(ped);
                        DropProp(ref sceneProp);
                        DropProp(ref sceneMoney);
                        try { ped.Task.WanderAround(); } catch { }
                        Release(ped);
                        EndScene();
                    }
                }
                break;
        }
    }

    // ------------------------------------------------------------------ scene: punched (and the end of a robbery)
    void StartPunch(Ped ped, Ped me, int now, bool fromRob)
    {
        BeginScene(S_PUNCH, ped, now);
        punchFromRob = fromRob;
        if (fromRob)
        {
            try { ped.Task.ClearAll(); ped.Task.TurnTo(me); } catch { }
            NextPhase(1, now);
        }
        else
        {
            Grab(ped);
            // the product robber: sometimes armed
            if (robberWeapon.Trim().ToLowerInvariant() != "none" && rnd.Next(100) < productRobberPistolChance) GiveWeapon(ped, "pistol");
            try { ped.Task.GoTo(me); } catch { }
        }
    }

    void TickPunch(Ped me, int now)
    {
        Ped ped = scenePed;
        if (!Alive(ped)) { EndScene(); return; }
        int t = now - phaseStart;
        float d = ped.Position.DistanceTo(me.Position);
        switch (scenePhase)
        {
            case 0:     // closes in
                if (d <= 1.5f || t > 2500)
                {
                    try { ped.Task.ClearAll(); ped.Task.TurnTo(me); } catch { }
                    NextPhase(1, now);
                }
                break;
            case 1:     // swings
                if (!sceneFlag)
                {
                    sceneFlag = true;
                    Anim(ped, animNpcPunch);
                    Say(ped, speechHostile);
                }
                if (t >= punchHitMs)
                {
                    if (d > 2.8f || me.isInVehicle())
                    {
                        // out of reach: no punch. A robber still runs with what he took.
                        if (punchFromRob) Flee(ped, me); else Release(ped);
                        EndScene();
                        return;
                    }
                    Knock(me, ped);
                    if (!punchFromRob)
                    {
                        Thief th = AddThief(ped, rnd.Next(punchCashMin, punchCashMax + 1), now);
                        th.Drugs[sceneDrug] = sceneQty;
                        th.Prop = MakeProp(sceneDrug, sceneQty, ped);
                        stash[sceneDrug] = Math.Max(0, stash[sceneDrug] - sceneQty);
                        SaveStash();
                    }
                    Flee(ped, me);
                    NextPhase(2, now);
                }
                break;
            case 2:     // on the ground, then back up
                bool down = false;
                try { down = me.isRagdoll || me.isGettingUp; } catch { }
                if ((t >= knockMs && !down) || t > knockMs + 7000)
                {
                    PlayPlayer("Angry");
                    EndScene();
                }
                break;
        }
    }

    void Knock(Ped me, Ped by)
    {
        try
        {
            Vector3 dir = me.Position - by.Position;
            dir.Z = 0f;
            if (dir.Length() > 0.01f) dir.Normalize();
            me.Task.ClearAll();
            me.ForceRagdoll(knockMs, false);
            me.Velocity = dir * 3.5f;
            int hp = me.Health;
            if (punchDamage > 0 && hp > punchDamage + 10) me.Health = hp - punchDamage;
        }
        catch (Exception ex) { Log("Knock: " + ex.Message); }
    }

    void Flee(Ped ped, Ped me)
    {
        try
        {
            NoRagdollFor(ped, 4000);
            ped.Task.ClearAll();
            ped.Task.AlwaysKeepTask = true;
            ped.Task.FleeFromChar(me, false, 240000);
        }
        catch { }
    }

    // ------------------------------------------------------------------ scene: robbed
    void StartRob(Ped ped, Ped me, int now)
    {
        BeginScene(S_ROB, ped, now);
        robDeducted = false; friskFallback = false;
        Grab(ped);
        GiveWeapon(ped, robberWeapon.Trim().ToLowerInvariant() == "random"
            ? (rnd.Next(100) < cashRobberPistolChance ? "pistol" : "knife") : robberWeapon);
        Say(ped, speechRob);
        Later(1000, "Robbed");
        try
        {
            me.Task.ClearAll();
            me.Task.HandsUp(robMs + 9000);
            ped.Task.GoTo(me);
        }
        catch { }
    }

    void TickRob(Ped me, int now)
    {
        Ped ped = scenePed;
        if (!Alive(ped) || me.isInVehicle())
        {
            // dead robber: UpdateThieves has already turned whatever he took into a pickup
            try { me.Task.ClearAll(); } catch { }
            if (Alive(ped))
            {
                if (robDeducted) Flee(ped, me); else Release(ped);
            }
            EndScene();
            return;
        }

        // the player can break out at any point
        if (now - sceneStart > 800 && BreakInput())
        {
            try { me.Task.ClearAll(); } catch { }
            PlayPlayer("Angry");
            if (robDeducted) Flee(ped, me);                 // he has the goods: he runs, kill him to get them back
            else
            {
                try { ped.Task.ClearAll(); ped.Task.FightAgainst(me); } catch { }
                Hold(ped, 45000);                           // nothing taken yet: he fights
            }
            EndScene();
            return;
        }

        int t = now - phaseStart;
        float d = ped.Position.DistanceTo(me.Position);
        switch (scenePhase)
        {
            case 0:     // walks up with the weapon out
                if (d <= 1.35f || t > 3500)
                {
                    if (d > 4f)
                    {
                        try { me.Task.ClearAll(); } catch { }
                        Release(ped);
                        EndScene();
                        return;
                    }
                    // lined up: you turn to face him, he stands square in front of you at the frisk distance,
                    // and both halves of the pat-down (bouncer_a_frisk / clubber_frisked) start on the same frame
                    try { ped.Task.ClearAllImmediately(); me.Task.ClearAllImmediately(); } catch { }
                    try
                    {
                        me.Heading = HeadingTo(me.Position, ped.Position);
                        Vector3 at = me.GetOffsetPosition(new Vector3(0f, friskDistance, 0f));
                        GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", ped, at.X, at.Y, ped.Position.Z);
                        ped.Heading = me.Heading + 180f;
                    }
                    catch { }
                    NextPhase(1, now);
                }
                break;
            case 1:     // goes through the player's pockets
                if (t < 120) break;                                 // a frame to settle on the spot
                if (!sceneFlag)
                {
                    sceneFlag = true;
                    PlayV(me, animPlayerFrisked, true);
                    PlayV(ped, animNpcSearch, true);
                    lastAnim = now;
                }
                else if (t > 1500) LoopAnim(ped, animNpcSearch, now);
                // the pat-down pose for you could not be found: hands up instead
                if (!friskFallback && t > 2800 && !AnimPlaying(me, animPlayerFrisked))
                {
                    friskFallback = true;
                    try { me.Task.HandsUp(robMs); } catch { }
                }
                if (!robDeducted && t >= robMs / 2)
                {
                    robDeducted = true;
                    int money = Player.Money;
                    int cash = Math.Min(money, rnd.Next(robCashMin, robCashMax + 1));
                    if (cash < 0) cash = 0;
                    Player.Money = money - cash;
                    Thief th = AddThief(ped, cash, now);
                    for (int i = 0; i < 3; i++) { th.Drugs[i] = stash[i]; stash[i] = 0; }
                    SaveStash();
                }
                if (t >= robMs)
                {
                    try { me.Task.ClearAll(); } catch { }
                    StartPunch(ped, me, now, true);         // full search done: he drops you and runs
                }
                break;
        }
    }

    void GiveWeapon(Ped ped, string name)
    {
        try
        {
            string n = name.Trim().ToLowerInvariant();
            if (n == "random") n = rnd.Next(2) == 0 ? "knife" : "pistol";   // (callers normally decide first)
            int id = 0;
            if (n == "knife") id = (int)Weapon.Melee_Knife;
            else if (n == "pistol" || n == "glock") id = (int)Weapon.Handgun_Glock;
            if (id == 0) return;
            GTA.Native.Function.Call("GIVE_WEAPON_TO_CHAR", ped, id, 60, 0);
            GTA.Native.Function.Call("SET_CURRENT_CHAR_WEAPON", ped, id, 1);
        }
        catch (Exception ex) { Log("GiveWeapon: " + ex.Message); }
    }

    // ------------------------------------------------------------------ hooker sting
    // 30% of hookers are undercover: she turns you down (one of her SOLICIT_TURNED_DOWN lines),
    // runs off, and squad cars come for you. From there it is the game's own police and arrest.
    Ped stingPed;
    int stingAt, stingStage, stingLineAt;     // stage 1: her line, 2: your line, then wanted + squad cars
    bool stingHeard;

    void StartSting(Ped ped, Ped me, int now)
    {
        try
        {
            Grab(ped);
            ped.Task.TurnTo(me);
        }
        catch (Exception ex) { Log("Sting: " + ex.Message); }

        // her own voice: one of the three at random, the plain context only if her voice has none of them
        string[] order = (string[])speechHookerSting.Clone();
        for (int i = order.Length - 1; i > 0; i--) { int k = rnd.Next(i + 1); string tmp = order[i]; order[i] = order[k]; order[k] = tmp; }
        string[] lines = new string[order.Length + 1];
        order.CopyTo(lines, 0);
        lines[order.Length] = "SOLICIT_TURNED_DOWN";
        Speech sp = new Speech();
        sp.Ped = ped; sp.Lines = lines; sp.Index = 0; sp.Next = now + 150; sp.Born = now;
        speeches.Add(sp);

        stingPed = ped; stingAt = now; stingHeard = false; stingStage = 1;
    }

    // once her line is over (5 s at most) she runs; then your line; once it is over you are wanted and the
    // squad cars come. From there the script lets go: it is the game's own police.
    void UpdateSting(Ped me, int now)
    {
        if (stingStage == 1)
        {
            Ped ped = stingPed;
            bool alive = Alive(ped);
            bool talking = false;
            if (alive) { try { talking = GTA.Native.Function.Call<bool>("IS_AMBIENT_SPEECH_PLAYING", ped); } catch { } }
            if (talking) stingHeard = true;
            if (alive && !(stingHeard && !talking) && now - stingAt < 5000 && now >= stingAt) return;

            stingPed = null;
            if (alive) { Flee(ped, me); Hold(ped, 30000); }
            else Release(ped);
            stingStage = 0;
            // Niko: IV\Angry\ANGRY7.wav, Johnny: TLAD\Angry\ANGRY1.wav, Luis: TBoGT\Angry\ANGRY3.wav
            {
                string file = episodeName == "TLAD" ? "ANGRY1.wav" : episodeName == "TBoGT" ? "ANGRY3.wav" : "ANGRY7.wav";
                string line = Path.Combine("Angry", file);
                if (File.Exists(Path.Combine(Path.Combine(baseDir, episodeName), line))) { PlayPlayer(line); stingStage = 2; stingLineAt = now; return; }
                Log("Missing voice file: " + Path.Combine(Path.Combine(baseDir, episodeName), line));
            }
        }
        else if (stingStage == 2)
        {
            // your line has played to its end (or 6 s have gone by)
            bool waiting = delayed.Count > 0 || PlayerTalking();
            if (waiting && now - stingLineAt < 6000 && now >= stingLineAt) return;
            if (now - stingLineAt < 300 && now >= stingLineAt) return;
            stingStage = 0;
        }
        else return;

        try { if (Player.WantedLevel < 1) Player.WantedLevel = 1; } catch { }
        for (int i = 0; i < stingSquadCars; i++) SpawnSquad(me, now, i);
    }

    void SpawnSquad(Ped me, int now, int index)
    {
        try
        {
            // a street spot 35 m+ away (a few tries, so every car comes)
            Vector3 pos = Vector3.Zero;
            bool found = false;
            for (int t = 0; t < 6 && !found; t++)
            {
                pos = World.GetNextPositionOnStreet(me.Position.Around(75f + index * 20f + t * 10f));
                found = pos.DistanceTo(me.Position) >= 35f;
            }
            if (!found) return;

            Model carModel = Model.BasicPoliceCarModel;
            if (!carModel.isValid) carModel = new Model(ModelHash("POLICE"));
            Model copModel = Model.CurrentCopModel;
            if (!copModel.isValid) copModel = new Model(ModelHash("M_Y_COP"));

            Vehicle car = World.CreateVehicle(carModel, pos);
            if (car == null || !Exists(car)) return;
            Vector3 dir = me.Position - pos;
            car.Heading = dir.ToHeading();

            Squad sq = new Squad();
            sq.Car = car; sq.Born = now;
            sq.Driver = car.CreatePedOnSeat(VehicleSeat.Driver, copModel, RelationshipGroup.Cop);
            sq.Partner = car.CreatePedOnSeat(VehicleSeat.RightFront, copModel, RelationshipGroup.Cop);
            car.SirenActive = true;
            if (sq.Driver != null) sq.Driver.Task.DriveTo(me, 45f, false);
            squads.Add(sq);
        }
        catch (Exception ex) { Log("Squad car: " + ex.Message); }
    }

    // once a squad car is close (or after 20 s) it is handed to the game's own wanted-level AI.
    // Its lights and siren stay on while you are wanted (the game's police chase / arrest you as usual).
    class SirenCar { public Vehicle Car; public int Until; }
    readonly List<SirenCar> sirenCars = new List<SirenCar>();
    int sirenCheckAt;

    void UpdateSquads(Ped me, int now)
    {
        for (int i = squads.Count - 1; i >= 0; i--)
        {
            Squad sq = squads[i];
            bool gone = sq.Car == null || !Exists(sq.Car);
            if (!gone && now - sq.Born < 20000 && now >= sq.Born && sq.Car.Position.DistanceTo(me.Position) > 40f) continue;
            try { if (sq.Driver != null && Exists(sq.Driver)) sq.Driver.NoLongerNeeded(); } catch { }
            try { if (sq.Partner != null && Exists(sq.Partner)) sq.Partner.NoLongerNeeded(); } catch { }
            try { if (!gone) sq.Car.NoLongerNeeded(); } catch { }
            if (!gone) { SirenCar sc = new SirenCar(); sc.Car = sq.Car; sc.Until = now + 300000; sirenCars.Add(sc); }
            squads.RemoveAt(i);
        }
        if (sirenCars.Count == 0 || (now - sirenCheckAt < 1000 && now >= sirenCheckAt)) return;
        sirenCheckAt = now;
        int wanted = 0;
        try { wanted = Player.WantedLevel; } catch { }
        for (int i = sirenCars.Count - 1; i >= 0; i--)
        {
            SirenCar sc = sirenCars[i];
            bool ok = false;
            try { ok = sc.Car != null && Exists(sc.Car); } catch { }
            if (!ok || wanted == 0 || now >= sc.Until || now < sc.Until - 600000) { sirenCars.RemoveAt(i); continue; }
            try { if (!sc.Car.SirenActive) sc.Car.SirenActive = true; } catch { }
        }
    }

    // ------------------------------------------------------------------ thieves and their loot
    Thief AddThief(Ped ped, int cash, int now)
    {
        Thief th = new Thief();
        th.Ped = ped; th.Cash = cash; th.Since = now;
        try { ped.Money = 0; } catch { }           // the loot pickup is the only cash he drops
        thieves.Add(th);
        return th;
    }

    void UpdateThieves(Ped me, int now)
    {
        for (int i = thieves.Count - 1; i >= 0; i--)
        {
            Thief th = thieves[i];
            bool exists = th.Ped != null && Exists(th.Ped);
            if (!exists)
            {
                RemoveThief(i);
                continue;
            }
            if (!th.Ped.isAlive)
            {
                DropLoot(th, now);
                RemoveThief(i);
                if (scenePed != null && scenePed.GetHashCode() == th.Ped.GetHashCode() && scene != S_NONE)
                {
                    try { me.Task.ClearAll(); } catch { }
                    EndScene();
                }
                if (th.Ped.Position.DistanceTo(me.Position) < 70f) Later(2000, "AngryKill");
                Release(th.Ped);
                continue;
            }
            bool inScene = scene != S_NONE && scenePed != null && scenePed.GetHashCode() == th.Ped.GetHashCode();
            // he runs; but if you attack him (hit him, shoot at him, swing at him) he turns and fights back with
            // whatever he has - pistol, knife or his fists
            if (!inScene) ThiefFightBack(th, me, now);
            if (!inScene && (th.Ped.Position.DistanceTo(me.Position) > 230f || now - th.Since > 300000 || now < th.Since))
            {
                Release(th.Ped);
                RemoveThief(i);
            }
        }
    }

    void ThiefFightBack(Thief th, Ped me, int now)
    {
        Ped p = th.Ped;
        if (th.Fighting) return;
        bool attacked = false;
        try { attacked = GTA.Native.Function.Call<bool>("HAS_CHAR_BEEN_DAMAGED_BY_CHAR", p, me, false); } catch { }
        if (!attacked)
        {
            try
            {
                float d = p.Position.DistanceTo(me.Position);
                bool aimedAt = GTA.Native.Function.Call<bool>("IS_PLAYER_FREE_AIMING_AT_CHAR", Player, p)
                    || GTA.Native.Function.Call<bool>("IS_PLAYER_TARGETTING_CHAR", Player, p);
                if (me.isShooting && (aimedAt || d < 8f)) attacked = true;                       // shooting at him / right by him
                else if (d < 2.5f && GTA.Native.Function.Call<bool>("IS_CHAR_IN_MELEE_COMBAT", me)) attacked = true;   // swinging at him
            }
            catch { }
        }
        if (!attacked) return;
        th.Fighting = true; th.FightAt = now;
        DropProp(ref th.Prop);                       // the hand is for the weapon now (what he took is still on him)
        try
        {
            p.Task.ClearAll();
            p.Task.AlwaysKeepTask = true;
            int w = 0;
            try { w = (int)p.Weapons.CurrentType; } catch { }
            if (w == (int)Weapon.Unarmed)
            {
                // he was running with it holstered: out it comes (pistol first, then the knife)
                try { if (p.Weapons.Glock.isPresent) GTA.Native.Function.Call("SET_CURRENT_CHAR_WEAPON", p, (int)Weapon.Handgun_Glock, 1); } catch { }
                try { if ((int)p.Weapons.CurrentType == (int)Weapon.Unarmed && p.Weapons.Knife.isPresent) GTA.Native.Function.Call("SET_CURRENT_CHAR_WEAPON", p, (int)Weapon.Melee_Knife, 1); } catch { }
            }
            p.Task.FightAgainst(me);
        }
        catch (Exception ex) { Log("Thief fight back: " + ex.Message); }
        Log("Thief attacked: he fights back");
    }

    void RemoveThief(int i)
    {
        Thief th = thieves[i];
        DropProp(ref th.Prop);
        thieves.RemoveAt(i);
    }

    void DropLoot(Thief th, int now)
    {
        try
        {
            Loot lt = new Loot();
            lt.Pos = th.Ped.Position;
            lt.Born = now;
            for (int i = 0; i < 3; i++) lt.Drugs[i] = th.Drugs[i];
            lt.Pickup = Pickup.CreateMoneyPickup(lt.Pos, Math.Max(1, th.Cash));
            loots.Add(lt);
        }
        catch (Exception ex) { Log("DropLoot: " + ex.Message); }
    }

    int lootMoneyLast = -1;
    bool lootMoneyUp;

    void UpdateLoot(Ped me, int now)
    {
        int cash = lootMoneyLast;
        try { cash = Player.Money; } catch { }
        lootMoneyUp = lootMoneyLast >= 0 && cash > lootMoneyLast;
        lootMoneyLast = cash;
        for (int i = loots.Count - 1; i >= 0; i--)
        {
            Loot lt = loots[i];
            bool present = false, collected = false;
            try
            {
                present = lt.Pickup != null && Exists(lt.Pickup);
                if (present) collected = lt.Pickup.HasBeenCollected;
            }
            catch { }

            float d = me.Position.DistanceTo(lt.Pos);
            if (collected || (!present && d < 6f) || (lootMoneyUp && d < 3f))
            {
                int back = 0;
                bool[] got = new bool[3];
                for (int k = 0; k < 3; k++)
                {
                    int room = Math.Max(0, maxCarry[k] - stash[k]);
                    int add = Math.Min(room, lt.Drugs[k]);
                    stash[k] += add; back += add;
                    got[k] = add > 0;
                }
                if (back > 0) SaveStash();
                if (lt.Dealer) { got[0] = true; got[1] = true; got[2] = true; }
                if (back > 0 || lt.Dealer) ShowPickupIcons(got);
                Log("Loot picked up: +" + back + " drugs" + (lt.Dealer ? " (dealer drop)" : ""));
                loots.RemoveAt(i);
            }
            else if (!present || now - lt.Born > 300000 || now < lt.Born)
            {
                try { if (present) lt.Pickup.Delete(); } catch { }
                loots.RemoveAt(i);
            }
        }
    }

    void Cleanup(int now)
    {
        lastCleanup = now;
        List<int> dead = new List<int>();
        foreach (KeyValuePair<int, Offer> kv in offers)
            if (now - kv.Value.Born > 600000 || now < kv.Value.Born) dead.Add(kv.Key);
        foreach (int h in dead) offers.Remove(h);
    }

    // ------------------------------------------------------------------ police search (StopRightThere)
    // StopRightThere calls BeginSearch2 the moment its officer reaches your car / bike window.
    // true = Pusher won the roll and runs the search from here (StopRightThere waits on Pusher.SearchState):
    //   1 running   2 nothing found, you are back in / on your vehicle: StopRightThere carries on with the ticket
    //   3 Pusher keeps the officers for good (refused / arrested / ride to HQ)   4 you drove off before getting out
    public bool BeginSearch2(Ped cop, Ped partner, Vehicle vehicle, Vehicle copCar)
    {
        try
        {
            if (!ticketCompat || !inited || scene != S_NONE || pendingSearch) return false;
            if (cop == null || vehicle == null || !Exists(cop) || !Exists(vehicle)) return false;
            bool bike = vehicle.Model.isBike;
            if (!bike && !vehicle.Model.isCar) return false;
            if (rnd.Next(100) >= searchChance) return false;
            pendingSearchCop = cop; pendingSearchVeh = vehicle;
            pendingSearchPartner = partner != null && Exists(partner) ? partner : null;
            pendingSquadCar = copCar != null && Exists(copCar) ? copCar : null;
            pendingSearch = true;
            SetSearchState(1);
            Log("Search won the roll (" + searchChance + "%): " + (bike ? "pat-down" : "trunk search"));
            return true;
        }
        catch (Exception ex) { Log("BeginSearch2: " + ex.Message); return false; }
    }

    // older bridge (no partner / squad car given)
    public bool BeginSearch(Ped cop, Vehicle vehicle) { return BeginSearch2(cop, null, vehicle, null); }

    // trunk / door / side spots around a vehicle
    Vector3 VehDims(Vehicle v)
    {
        Vector3 dim = new Vector3(2.0f, 4.6f, 1.5f);
        try
        {
            Vector3 got = v.Model.GetDimensions();
            if (got.X > 0.5f && got.Y > 1.0f) dim = got;
        }
        catch { }
        return dim;
    }

    Vector3 CarSpot(Vehicle v, float side, float fwd)
    {
        Vector3 d = VehDims(v);
        float x = side == 0f ? 0f : (side > 0 ? d.X * 0.5f + side : -(d.X * 0.5f) + side);
        float y = fwd == 0f ? 0f : (fwd > 0 ? d.Y * 0.5f + fwd : -(d.Y * 0.5f) + fwd);
        return v.GetOffsetPosition(new Vector3(x, y, 0f));
    }

    bool playerLocked, copChoice;
    int choiceKind;                // 1 refuse / comply (LB / RB)   2 found: comply / refuse (D-pad left / right, LT)   3 pick the cuffs
    Ped searchPartner, pendingSearchPartner;
    Vehicle squadCar, pendingSquadCar;
    VehicleSeat squadSeat = VehicleSeat.LeftRear;
    bool searchFound, cuffsBroken, partnerUnder, cuffPinned, partnerLook, rearDoorOpened, rearDoorClosed;
    int stuckTries, reverseUntil;
    int partnerWalkAt, partnerCheckAt;
    Vector3 partnerLastPos;
    int partnerUnderAt, stuckSince, walkCheckAt, walkTries, doorCheckAt, cuffLeg;
    Vector3 lastWalkPos, doorSpot, cuffSide;
    int outAt, meInAt, lastAlignAt, pinFastUntil;
    bool doorClosing, copPinned, copAtDoor, headPinned, windowPoseHeld;
    int windowPoseAt, cuffComp;
    bool cuffEnded;
    static bool cuffOffKnown;
    static float cuffOffF, cuffOffR;
    Vector3 cuffHips0;
    float windowPoseTime = -1f;
    Vector3 copClearSpot, copHandleSpot, seatSpot, entryHips;
    int doorOpenStart, sitStart, armsAt, inCarAt;
    bool getInSeen;
    float sitZ;
    Vector3 lastCopPos;
    bool copTurned;
    Vector3 escortTarget, copSideSpot;
    bool partnerToCar, partnerEntering, copToDriver, getInArmsDone;
    int copDriverAt, copCheckAt, getInArmsAt, copToHandleAt, copHandleTries;
    float rearOpenAngle;
    float headPhase = -1f, doorStartAngle, cuffZ;
    int cuffLogAt, cuffCheckAt, cuffMiss, cuffReapplyAt, cuffReapplyLogs;
    float cuffHipDistance = 0.5f;
    float getInHeadingOffset = 0f;
    static readonly string[] GetInSets = { "", "veh@std", "veh@low", "veh@van", "veh@truck", "veh@bus" };
    int getInSetIdx, getInTryAt;
    bool getInPlaying;            // which way you face when get_in_ds starts (0 = the way the car faces)
    int handsUpToLoopMs = 1500;               // the game's hands-up gets this long before armsup_loop takes over
    string[] headAnim;
    Vector3 turnTarget;
    Vector3 copDoorSpot;
    float escortSide = 1f;
    int escortAt;
    float cuffPhase = -1f;
    int pickCount, lastPickAt, rideStart, lastDriveOrder, lastPinAt, yAt;
    Vector3 searchSpot, asideSpot, hqTarget;
    bool hqKnown;
    List<int> policeHashes;
    readonly List<Vector3> hqSpots = new List<Vector3>();

    // controls off, camera (right stick) still free
    void LockPlayer(bool lockIt)
    {
        try
        {
            if (lockIt)
            {
                GTA.Native.Function.Call("SET_CAMERA_CONTROLS_DISABLED_WITH_PLAYER_CONTROLS", false);
                Player.CanControlCharacter = false;
            }
            else
            {
                Player.CanControlCharacter = true;
                GTA.Native.Function.Call("SET_CAMERA_CONTROLS_DISABLED_WITH_PLAYER_CONTROLS", true);
            }
        }
        catch (Exception ex) { Log("LockPlayer: " + ex.Message); }
        playerLocked = lockIt;
    }

    void PoliceIgnore(bool on)
    {
        try { GTA.Native.Function.Call("SET_POLICE_IGNORE_PLAYER", GTA.Native.Function.Call<int>("GET_PLAYER_ID"), on); }
        catch (Exception ex) { Log("PoliceIgnore: " + ex.Message); }
    }

    void SayArrest(Ped cop)
    {
        int k = rnd.Next(2);
        if (k == 0) CopSay(cop, "ARREST_PED", 2);
        else CopSay(cop, "ARREST_PLAYER", 3);
    }

    void WalkStraight(Ped p, Vector3 to)
    {
        try { GTA.Native.Function.Call("TASK_GO_STRAIGHT_TO_COORD", p, to.X, to.Y, to.Z, 2, 20000); } catch { }
    }

    void PlaceAt(Ped p, Vector3 at, float heading)
    {
        try
        {
            Vector3 cur = p.Position;
            if (cur.DistanceTo(at) < 2.5f) GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", p, at.X, at.Y, cur.Z);
            p.Heading = heading;
        }
        catch { }
    }

    // plays an animation and checks a moment later that it really runs; if not, the same name is tried
    // in the other likely sets. Pusher.log says which set worked (or that none did).
    class AnimCheck { public Ped P; public string[] A; public List<string> Sets; public int Idx, CheckAt; public bool Loop; }
    readonly List<AnimCheck> animChecks = new List<AnimCheck>();
    static readonly string[] AnimFallbackSets = { "cop", "amb@car_stash", "cop_search_idles", "arrest", "amb@bridgecops", "amb@default" };

    void PlayV(Ped p, string[] a, bool loop)
    {
        if (p == null || a == null || a.Length < 2) return;
        for (int i = animChecks.Count - 1; i >= 0; i--) if (animChecks[i].P == p) animChecks.RemoveAt(i);
        AnimCheck c = new AnimCheck();
        c.P = p; c.A = a; c.Loop = loop; c.Idx = 0;
        c.Sets = new List<string>();
        c.Sets.Add(a[0]);
        foreach (string f in AnimFallbackSets) if (!c.Sets.Contains(f)) c.Sets.Add(f);
        PlaySet(p, c.Sets[0], a[1], loop);
        c.CheckAt = Game.GameTime + 350;
        animChecks.Add(c);
    }

    void PlaySet(Ped p, string set, string name, bool loop)
    {
        try
        {
            AnimationSet s;
            if (!animSets.TryGetValue(set, out s)) { s = new AnimationSet(set); animSets[set] = s; }
            p.Task.PlayAnimation(s, name, 8f, loop ? AnimationFlags.Unknown05 : AnimationFlags.None);
        }
        catch (Exception ex) { Log("Anim " + set + " / " + name + ": " + ex.Message); }
    }

    void UpdateAnimChecks(int now)
    {
        for (int i = animChecks.Count - 1; i >= 0; i--)
        {
            AnimCheck c = animChecks[i];
            if (now < c.CheckAt) continue;
            if (!Alive(c.P)) { animChecks.RemoveAt(i); continue; }
            string set = c.Sets[c.Idx];
            bool playing = false;
            try { playing = GTA.Native.Function.Call<bool>("IS_CHAR_PLAYING_ANIM", c.P, set, c.A[1]); } catch { }
            if (playing)
            {
                if (c.Idx > 0) { Log("Animation " + c.A[1] + " found in set '" + set + "' (not '" + c.A[0] + "') - using that"); c.A[0] = set; }
                else Log("Animation OK: " + set + " / " + c.A[1]);
                animChecks.RemoveAt(i);
                continue;
            }
            c.Idx++;
            if (c.Idx >= c.Sets.Count)
            {
                Log("Animation NOT FOUND in any set: " + c.A[1] + " (tried " + string.Join(", ", c.Sets.ToArray()) + ")");
                animChecks.RemoveAt(i);
                continue;
            }
            try { GTA.Native.Function.Call("REQUEST_ANIMS", c.Sets[c.Idx]); } catch { }
            PlaySet(c.P, c.Sets[c.Idx], c.A[1], c.Loop);
            c.CheckAt = now + 350;
        }
    }

    // straight behind the player, his back to the officer's front, no matter how far off he stopped
    // which way the body really faces (an animation can turn it away from the ped's heading):
    // taken from the hips, so "behind" is behind his back as you see it
    Vector3 BodyForward(Ped p)
    {
        try
        {
            Vector3 l = p.GetBonePosition(Bone.LeftThigh), r = p.GetBonePosition(Bone.RightThigh);
            float rx = r.X - l.X, ry = r.Y - l.Y;
            float len = (float)Math.Sqrt(rx * rx + ry * ry);
            if (len > 0.05f) return new Vector3(-ry / len, rx / len, 0f);
        }
        catch { }
        Vector3 f = p.GetOffsetPosition(new Vector3(0f, 1f, 0f)) - p.Position;
        float fl = (float)Math.Sqrt(f.X * f.X + f.Y * f.Y);
        return fl > 0.01f ? new Vector3(f.X / fl, f.Y / fl, 0f) : new Vector3(0f, 1f, 0f);
    }

    Vector3 Hips(Ped p)
    {
        try { return p.GetBonePosition(Bone.Pelvis); } catch { return p.Position; }
    }

    // the officer's hips straight behind yours, facing the same way as your body; he can't be pushed off the
    // spot (no collision with you, position held) until the cuffing is over
    void PutBehind(Ped cop, Ped me, float dist)
    {
        try
        {
            Vector3 f = BodyForward(me);
            Vector3 mh = Hips(me);
            Vector3 at = new Vector3(mh.X - f.X * dist, mh.Y - f.Y * dist, 0f);
            Vector3 cur = cop.Position;
            PinCop(cop, false);
            GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, at.X, at.Y, cur.Z);
            cop.Heading = HeadingTo(new Vector3(0f, 0f, 0f), f);
        }
        catch { }
    }

    void HoldCufferBehind(Ped cop, Ped me, int now)
    {
        try
        {
            Vector3 f = BodyForward(me);
            Vector3 mh = Hips(me), ch = Hips(cop);
            float wx = mh.X - f.X * cuffHipDistance, wy = mh.Y - f.Y * cuffHipDistance;
            float dx = wx - ch.X, dy = wy - ch.Y;
            Vector3 cur = cop.Position;
            // facing: his body turned to face the same way as yours
            Vector3 cf = BodyForward(cop);
            float turn = (float)(Math.Atan2(cf.X * f.Y - cf.Y * f.X, cf.X * f.X + cf.Y * f.Y) * 180.0 / Math.PI);
            if (dx * dx + dy * dy > 0.0004f || Math.Abs(turn) > 1f)
            {
                try { GTA.Native.Function.Call("FREEZE_CHAR_POSITION", cop, false); } catch { }
                GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, cur.X + dx, cur.Y + dy, cuffZ);
                cop.Heading = cop.Heading + turn;
                try { GTA.Native.Function.Call("FREEZE_CHAR_POSITION", cop, true); } catch { }
            }
            if (now - cuffLogAt > 700)
            {
                cuffLogAt = now;
                Vector3 mL = me.GetBonePosition(Bone.LeftHand), mR = me.GetBonePosition(Bone.RightHand);
                Vector3 cL = cop.GetBonePosition(Bone.LeftHand), cR = cop.GetBonePosition(Bone.RightHand);
                Vector3 mHands = new Vector3((mL.X + mR.X) * 0.5f, (mL.Y + mR.Y) * 0.5f, 0f);
                Vector3 cHands = new Vector3((cL.X + cR.X) * 0.5f, (cL.Y + cR.Y) * 0.5f, 0f);
                Log("Cuffing: hips " + S(Dist2(Hips(cop), mh)) + " m apart, hands " + S(Dist2(mHands, cHands))
                    + " m apart, his root moved " + S((float)Math.Sqrt(dx * dx + dy * dy)) + " m this frame");
            }
        }
        catch (Exception ex) { Log("Holding the officer behind you: " + ex.Message); }
    }

    void PinCop(Ped cop, bool on)
    {
        if (!Alive(cop)) return;
        try { GTA.Native.Function.Call("SET_CHAR_COLLISION", cop, !on); } catch { }
        try { GTA.Native.Function.Call("FREEZE_CHAR_POSITION", cop, on); } catch { }
        copPinned = on;
    }

    // during the animation: wherever the animation has carried his body, it is moved back so his hips
    // are exactly behind yours
    void AlignCuffer(Ped cop, Ped me, float dist, int now)
    {
        if (now - lastAlignAt < 250) return;
        lastAlignAt = now;
        try
        {
            Vector3 f = BodyForward(me);
            Vector3 mh = Hips(me), ch = Hips(cop);
            float wx = mh.X - f.X * dist, wy = mh.Y - f.Y * dist;
            float dx = wx - ch.X, dy = wy - ch.Y;
            if (dx * dx + dy * dy < 0.05f * 0.05f) return;
            Vector3 cur = cop.Position;
            bool was = copPinned;
            try { GTA.Native.Function.Call("FREEZE_CHAR_POSITION", cop, false); } catch { }
            GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, cur.X + dx, cur.Y + dy, cur.Z);
            if (was) { try { GTA.Native.Function.Call("FREEZE_CHAR_POSITION", cop, true); } catch { } }
        }
        catch { }
    }

    // he walks round your side first, so he never pushes through you, then steps in behind
    void StartCuffApproach(Ped me, Ped cop, int now)
    {
        try
        {
            Vector3 f = BodyForward(me);
            Vector3 right = new Vector3(f.Y, -f.X, 0f);
            Vector3 rel = cop.Position - me.Position;
            float sideSign = (rel.X * right.X + rel.Y * right.Y) >= 0f ? 1f : -1f;
            Vector3 mp = me.Position;
            cuffSide = new Vector3(mp.X + right.X * 0.9f * sideSign - f.X * 0.7f, mp.Y + right.Y * 0.9f * sideSign - f.Y * 0.7f, mp.Z);
        }
        catch { cuffSide = me.Position; }
        cuffLeg = 0; walkCheckAt = now;
        try { cop.Task.ClearAllImmediately(); } catch { }
        WalkStraight(cop, cuffSide);
    }

    // crim_cuffed on the upper body only, held on the hands-behind-the-back frame
    void CuffUpperBody(Ped me, int now)
    {
        try { GTA.Native.Function.Call("TASK_PLAY_ANIM_SECONDARY_UPPER_BODY", me, animCrimCuffed[1], animCrimCuffed[0], 1000.0f, 1, 0, 0, 0, -1); }
        catch (Exception ex) { Log("Cuffed upper body: " + ex.Message); }
        cuffPinned = true;
        pinFastUntil = now + 1200;
        lastPinAt = 0;
        if (cuffPhase > 0f) { try { GTA.Native.Function.Call("SET_CHAR_ANIM_CURRENT_TIME", me, animCrimCuffed[0], animCrimCuffed[1], cuffPhase); } catch { } }
        AnimSpeedOf(me, animCrimCuffed, 0f);
    }

    // where he walks up to before the cuffing: where his body starts cop_cuff (learned the first time),
    // otherwise one footstep behind you
    Vector3 CuffApproachSpot(Ped me)
    {
        if (!cuffOffKnown) return me.GetOffsetPosition(new Vector3(0f, -cuffDistance, 0f));
        Vector3 mp = me.Position, f = Fwd(me.Heading), r = Rgt(me.Heading);
        return new Vector3(mp.X + f.X * cuffOffF + r.X * cuffOffR, mp.Y + f.Y * cuffOffF + r.Y * cuffOffR, mp.Z);
    }

    static Vector3 Fwd(float heading)
    {
        double a = heading * Math.PI / 180.0;
        return new Vector3((float)-Math.Sin(a), (float)Math.Cos(a), 0f);
    }
    static Vector3 Rgt(float heading)
    {
        double a = heading * Math.PI / 180.0;
        return new Vector3((float)Math.Cos(a), (float)Math.Sin(a), 0f);
    }

    void ShiftCop(Ped cop, float dx, float dy)
    {
        try
        {
            Vector3 c = cop.Position;
            GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, c.X + dx, c.Y + dy, cuffZ != 0f ? cuffZ : c.Z);
        }
        catch { }
    }

    void ReleaseWindowPose(Ped cop)
    {
        windowPoseHeld = false; windowPoseAt = 0;
        try { GTA.Native.Function.Call("BLOCK_CHAR_GESTURE_ANIMS", cop, false); } catch { }
    }

    // armsup_2_searched_pose (or searched_pose) on the upper body only, held on the hands-on-head frame
    void HeadUpperBody(Ped me, int now)
    {
        headAnim = AnimPlaying(me, animArmsToSearched) ? animArmsToSearched : animSearchedPose;
        headPhase = AnimPhase(me, headAnim);
        if (headPhase <= 0.01f && headAnim == animArmsToSearched) headPhase = handsOnHeadPhase;
        try { GTA.Native.Function.Call("TASK_PLAY_ANIM_SECONDARY_UPPER_BODY", me, headAnim[1], headAnim[0], 1000.0f, 1, 0, 0, 0, -1); }
        catch (Exception ex) { Log("Hands on head upper body: " + ex.Message); }
        headPinned = true;
        pinFastUntil = now + 1200;
        lastPinAt = 0;
        if (headPhase > 0f) { try { GTA.Native.Function.Call("SET_CHAR_ANIM_CURRENT_TIME", me, headAnim[0], headAnim[1], headPhase); } catch { } }
        AnimSpeedOf(me, headAnim, 0f);
    }

    // TASK_GO_STRAIGHT_TO_COORD stops short of its target: aim a metre past it, the caller stops him on it
    void WalkThrough(Ped p, Vector3 to)
    {
        try
        {
            Vector3 from = p.Position;
            float dx = to.X - from.X, dy = to.Y - from.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.05f) { WalkStraight(p, to); return; }
            WalkStraight(p, new Vector3(to.X + dx / len, to.Y + dy / len, to.Z));
        }
        catch { WalkStraight(p, to); }
    }

    static float Dist2(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    // the officer straight behind you, facing your back, at the given distance, and locked there
    // (no collision with you, position frozen) until the cuffs are on
    void LockBehind(Ped cop, Ped me, float dist)
    {
        try
        {
            Vector3 at = me.GetOffsetPosition(new Vector3(0f, -dist, 0f));
            Vector3 cur = cop.Position;
            PinCop(cop, false);
            GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, at.X, at.Y, cur.Z);
            cop.Heading = me.Heading;
            PinCop(cop, true);
        }
        catch (Exception ex) { Log("LockBehind: " + ex.Message); }
    }

    // the officer walks with you: at your side, half a step behind (re-ordered every few seconds)
    void Escort(Ped cop, Ped me, int now)
    {
        escortAt = now;
        // a walk (never a run) to a point beside you and a step ahead of where you are heading; it is only
        // given again when that point has moved, so he keeps walking instead of stopping and starting
        try
        {
            Vector3 f = BodyForward(me), r = new Vector3(f.Y, -f.X, 0f), mp = me.Position;
            Vector3 at = new Vector3(mp.X + r.X * 1.0f * escortSide + f.X * 1.2f, mp.Y + r.Y * 1.0f * escortSide + f.Y * 1.2f, mp.Z);
            if (Dist2(at, escortTarget) > 0.6f || escortTarget.X == 0f)
            {
                escortTarget = at;
                WalkStraight(cop, at);
            }
        }
        catch { }
    }

    // just behind the squad car's rear corner on the door side (clear of the door's swing)
    Vector3 WaitBySquadDoor(Vehicle v, VehicleSeat seat)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f + 0.35f;
        if (seat == VehicleSeat.LeftRear) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, -d.Y * 0.5f - 0.9f, 0f));
    }

    // out of your way once the door is open: straight out from the car, away from it, a little behind the door
    // (never into the open door, never on your walk to the doorway)
    Vector3 CopClearSpot(Vehicle v, VehicleSeat seat)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f + 2.6f;
        if (seat == VehicleSeat.LeftRear) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, -d.Y * 0.12f - 0.8f, 0f));
    }

    // the middle of the back seat on the door side, at hip level from outside
    Vector3 RearSeatSpot(Vehicle v, VehicleSeat seat)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f - 0.55f;
        if (seat == VehicleSeat.LeftRear) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, -d.Y * 0.12f - 0.15f, 0f));
    }

    float entryZ(Vector3 cur) { return sitZ != 0f ? sitZ : cur.Z; }

    // the rear door stays open (you can't pull it shut) until the officer shuts it
    void HoldRearOpen(VehicleDoor rear)
    {
        // the door is held by the script, every frame, fully open: nothing (your get-in included) can pull it
        try { GTA.Native.Function.Call("CONTROL_CAR_DOOR", squadCar, (int)rear, 1, 1.0f); } catch { }
        try { if (squadCar.Door(rear).Angle < 0.9f) squadCar.Door(rear).Angle = 1.0f; } catch { }
    }

    // where he stands to hold the rear door: past its back edge, out from the car
    // after opening the door: he takes a step out of your way and then stands still, turned to you
    void CopStandAside(Ped cop, Ped me, int now)
    {
        if (copTurned || doorClosing) return;
        bool st = false;
        if (now - copCheckAt > 400) { st = Dist2(cop.Position, lastCopPos) < 0.1f; copCheckAt = now; lastCopPos = cop.Position; }
        float d = Dist2(cop.Position, copClearSpot);
        if (d < 0.4f || (st && d < 1.2f) || now - escortAt > 4000)
        {
            copTurned = true;
            try { cop.Task.ClearAll(); cop.Task.TurnTo(me); } catch { }
        }
    }

    // where he shuts the open door from: just outside its open end
    Vector3 CopDoorSpot(Vehicle v, VehicleSeat seat)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f + 1.0f;
        if (seat == VehicleSeat.LeftRear) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, -d.Y * 0.12f - 0.65f, 0f));
    }

    // where he opens the door from: right at the door, by its handle (the back edge of the rear door)
    Vector3 CopHandleSpot(Vehicle v, VehicleSeat seat)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f + 0.5f;
        if (seat == VehicleSeat.LeftRear) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, -d.Y * 0.12f - 0.55f, 0f));
    }

    // a point on the car's middle line level with a spot: facing it means facing the car side square on
    Vector3 FacingCar(Vehicle v, Vector3 spot)
    {
        Vector3 c = v.Position, f = Fwd(v.Heading);
        float along = (spot.X - c.X) * f.X + (spot.Y - c.Y) * f.Y;
        return new Vector3(c.X + f.X * along, c.Y + f.Y * along, spot.Z);
    }

    // outside a front door (driver's when left, passenger's when right)
    Vector3 SideDoorSpot(Vehicle v, bool left)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f + 0.8f;
        if (left) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, d.Y * 0.1f, 0f));
    }

    // just outside the open rear doorway
    Vector3 DoorGapSpot(Vehicle v, VehicleSeat seat)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f + 0.45f;
        if (seat == VehicleSeat.LeftRear) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, -d.Y * 0.12f, 0f));
    }

    Vector3 RearDoorCentre(Vehicle v, VehicleSeat seat)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f;
        if (seat == VehicleSeat.LeftRear) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, -d.Y * 0.12f, 0f));
    }

    // outside the rear door on the given side, level with the back seat
    Vector3 RearDoorSpot(Vehicle v, VehicleSeat seat)
    {
        Vector3 d = VehDims(v);
        float sx = d.X * 0.5f + 0.75f;
        if (seat == VehicleSeat.LeftRear) sx = -sx;
        return v.GetOffsetPosition(new Vector3(sx, -d.Y * 0.12f, 0f));
    }

    // a walk that goes round things (the car) instead of through them
    // a quicker walk (never a run): the walk animation played faster. 1 = normal again.
    void WalkPace(Ped p, float k)
    {
        try { if (p != null && Exists(p)) GTA.Native.Function.Call("SET_CHAR_MOVE_ANIM_SPEED_MULTIPLIER", p, k); } catch { }
    }

    // the officer heads straight for the rear door on your side of the squad car. Only if he is not on that
    // side yet does he first go round the nearest corner (outside it), so he never walks into the car or you.
    Vector3 copWaypoint;
    float brakeV0 = -1f;          // squad car speed when it starts braking for the police HQ spot
    int stoppedAt, stationOutAt;
    const float HqBrake = 3.0f;   // m/s lost per second while pulling in
    bool copViaWaypoint;
    const float CopQuickWalk = 1.35f;

    void CopStraightToDoor(Ped cop)
    {
        copViaWaypoint = false;
        try
        {
            Vector3 d = VehDims(squadCar), c = squadCar.Position, f = Fwd(squadCar.Heading), rt = Rgt(squadCar.Heading);
            float side = squadSeat == VehicleSeat.LeftRear ? -1f : 1f;
            Vector3 p = cop.Position;
            float lx = (p.X - c.X) * rt.X + (p.Y - c.Y) * rt.Y;
            float ly = (p.X - c.X) * f.X + (p.Y - c.Y) * f.Y;
            if (side * lx < d.X * 0.5f + 0.3f)
            {
                copViaWaypoint = true;
                copWaypoint = ly >= 0f
                    ? squadCar.GetOffsetPosition(new Vector3(side * (d.X * 0.5f + 0.9f), d.Y * 0.5f + 0.9f, 0f))      // round the front corner
                    : squadCar.GetOffsetPosition(new Vector3(side * (d.X * 0.5f + 1.6f), -d.Y * 0.5f - 0.6f, 0f));    // round the back corner, outside where you wait
            }
        }
        catch { copViaWaypoint = false; }
        try { cop.Task.ClearAll(); } catch { }
        WalkPace(cop, CopQuickWalk);
        WalkStraight(cop, copViaWaypoint ? copWaypoint : copHandleSpot);
    }

    void WalkPath(Ped p, Vector3 to)
    {
        try { GTA.Native.Function.Call("TASK_FOLLOW_NAV_MESH_TO_COORD", p, to.X, to.Y, to.Z, 2, -1, 0.5f); }
        catch { WalkStraight(p, to); }
    }

    void CloseRear(VehicleDoor door)
    {
        try
        {
            if (squadCar == null || !Exists(squadCar)) return;
            if (squadCar.Door(door).isOpen || squadCar.Door(door).Angle > 0.05f) squadCar.Door(door).Close();
        }
        catch { }
    }

    bool AnimLoop(Ped p, string[] a)
    {
        if (a == null || a.Length < 2) return false;
        try
        {
            AnimationSet s;
            if (!animSets.TryGetValue(a[0], out s)) { s = new AnimationSet(a[0]); animSets[a[0]] = s; }
            p.Task.PlayAnimation(s, a[1], 8f, AnimationFlags.Unknown05);
            return true;
        }
        catch (Exception ex) { Log("AnimLoop " + a[0] + " / " + a[1] + ": " + ex.Message); return false; }
    }

    void AnimSpeedOf(Ped p, string[] a, float speed)
    {
        try { GTA.Native.Function.Call("SET_CHAR_ANIM_SPEED", p, a[0], a[1], speed); } catch { }
    }

    void StartSearch(Ped me, int now)
    {
        BeginScene(S_SEARCH, searchCop, now);
        searchBike = searchVeh.Model.isBike;
        searchPartner = pendingSearchPartner; pendingSearchPartner = null;
        squadCar = pendingSquadCar; pendingSquadCar = null;
        if (squadCar == null) squadCar = FindSquadCar(searchCop);
        // the officers never fall over during the search: bumping into you or the car must not knock them down
        NoRagdoll(searchCop, true);
        NoRagdoll(searchPartner, true);
        searchFound = false; cuffsBroken = false; partnerUnder = false; cuffPinned = false;
        partnerLook = false; rearDoorOpened = false; rearDoorClosed = false; stuckSince = 0; partnerWalkAt = 0;
        cuffPhase = -1f; pickCount = 0; lastPickAt = 0; yAt = 0;
        walkCheckAt = 0; walkTries = 0; doorCheckAt = 0; cuffLeg = 0; outAt = 0; meInAt = 0; doorClosing = false; copPinned = false; lastAlignAt = 0; stuckTries = 0; reverseUntil = 0; pinFastUntil = 0; copAtDoor = false; escortAt = 0; headPinned = false; headPhase = -1f;
        choiceKind = 1; copChoice = true;
        try
        {
            if (searchBike) searchCop.Task.TurnTo(me);
            else
            {
                // the window pose exactly as StopRightThere plays it at a ticket: squared up to the door,
                // car_chat_outside, then held 700 ms in (arm locked on the car) and pinned every frame
                float h = searchVeh.Heading - 90f;
                while (h < 0f) h += 360f;
                while (h >= 360f) h -= 360f;
                searchCop.Heading = h;
                try { GTA.Native.Function.Call("BLOCK_CHAR_GESTURE_ANIMS", searchCop, true); } catch { }
                AnimLoop(searchCop, animCopCarChat);
                windowPoseAt = now + 700; windowPoseHeld = false; windowPoseTime = -1f;
            }
        }
        catch { }
        CopSay(searchCop, "GET_OUT_OF_CAR_SHOUT", 4);
        if (!searchBike) CopSay(searchCop, "ASK_TO_OPEN_TRUNK", 2);
    }

    // you are done with the officers: they become ordinary police
    void ReleaseCop(Ped p)
    {
        if (p == null || !Exists(p)) return;
        NoRagdoll(p, false);
        try
        {
            p.Task.AlwaysKeepTask = false;
            p.BlockPermanentEvents = false;
            p.NoLongerNeeded();
        }
        catch { }
    }

    // refused / bailed / dropped off: the game's own 1-star wanted level takes it from here
    void SearchToVanilla(Ped me, bool arrestOrder)
    {
        ReleaseTraffic();
        WalkPace(searchCop, 1f);
        try { GTA.Native.Function.Call("FREEZE_CHAR_POSITION", me, false); } catch { }
        Ped cop = searchCop, partner = searchPartner;
        headPinned = false; cuffPinned = false;
        ReleaseWindowPose(cop);
        try { GTA.Native.Function.Call("SET_CHAR_COLLISION", me, true); } catch { }
        if (copPinned) PinCop(cop, false);
        try { me.Task.ClearSecondary(); } catch { }
        if (playerLocked) LockPlayer(false);
        PoliceIgnore(false);
        try { if (Player.WantedLevel < 1) Player.WantedLevel = 1; } catch { }
        ReleaseCop(cop);
        ReleaseCop(partner);
        if (arrestOrder && Alive(cop))
        {
            try { GTA.Native.Function.Call("TASK_CHAR_ARREST_CHAR", cop, me); } catch (Exception ex) { Log("Arrest order: " + ex.Message); }
        }
        try { if (squadCar != null && Exists(squadCar)) squadCar.NoLongerNeeded(); } catch { }
        choiceKind = 0; copChoice = false;
        SetSearchState(3);
        EndScene();
    }

    void EndSearch(int state)
    {
        ReleaseTraffic();
        WalkPace(searchCop, 1f);
        try { GTA.Native.Function.Call("FREEZE_CHAR_POSITION", Player.Character, false); } catch { }
        choiceKind = 0; copChoice = false;
        try { GTA.Native.Function.Call("SET_CHAR_COLLISION", Player.Character, true); } catch { }
        ReleaseWindowPose(searchCop);
        headPinned = false;
        if (copPinned) PinCop(searchCop, false);
        NoRagdoll(searchCop, false);
        NoRagdoll(searchPartner, false);
        try { Player.Character.Task.ClearSecondary(); } catch { }
        if (playerLocked) LockPlayer(false);
        SetSearchState(state);
        EndScene();
    }

    Vehicle FindSquadCar(Ped cop)
    {
        if (policeHashes == null)
        {
            policeHashes = new List<int>();
            string[] names = { "POLICE", "POLICE2", "POLICE3", "POLICE4", "POLPATRIOT", "NOOSE" };
            foreach (string n in names) { try { policeHashes.Add(ModelHash(n)); } catch { } }
        }
        Vehicle best = null;
        float bestD = 45f;
        try
        {
            foreach (Vehicle v in World.GetVehicles(cop.Position, 45f))
            {
                if (v == null || !Exists(v)) continue;
                if (searchVeh != null && v.GetHashCode() == searchVeh.GetHashCode()) continue;
                if (!policeHashes.Contains(v.Model.Hash)) continue;
                float d = v.Position.DistanceTo(cop.Position);
                if (d < bestD) { bestD = d; best = v; }
            }
        }
        catch (Exception ex) { Log("FindSquadCar: " + ex.Message); }
        return best;
    }

    // ---- police HQ drop-off spots (scripts\Pusher\policehq.txt)
    string HqFile() { return Path.Combine(baseDir, "policehq.txt"); }

    void LoadHq()
    {
        hqSpots.Clear();
        string path = HqFile();
        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path, "; PUSHER police HQ drop-off spots, one per line: (x, y, z, heading)" + Environment.NewLine
                    + "; Add a line for each spot where the squad car should park." + Environment.NewLine);
            }
            catch { }
            return;
        }
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == ';') continue;
            string[] p = line.Replace("(", "").Replace(")", "").Split(',');
            if (p.Length < 3) continue;
            hqSpots.Add(new Vector3(F(p[0], 0), F(p[1], 0), F(p[2], 0)));
        }
        Log("Police HQ spots: " + hqSpots.Count);
    }

    bool NearestHq(Vector3 from, out Vector3 hq)
    {
        hq = from;
        float best = float.MaxValue;
        foreach (Vector3 v in hqSpots)
        {
            float d = v.DistanceTo(from);
            if (d < best) { best = d; hq = v; }
        }
        return hqSpots.Count > 0;
    }

    void TickSearch(Ped me, int now)
    {
        Ped cop = searchCop;
        Vehicle veh = searchVeh;
        int t = now - phaseStart;
        // StopRightThere keeps the partner's walk-up and radio going while the search is still at the window
        try { AppDomain.CurrentDomain.SetData("Pusher.SearchPhase", scenePhase); } catch { }
        // traffic is held back from the whole scene until you are driven away
        if (scenePhase < 38 || scenePhase > 40) HaltTraffic(me, cop, now);
        else if (parkedCars.Count > 0 || halted.Count > 0) ReleaseTraffic();
        if (!Alive(cop) || (veh == null || !Exists(veh)) && scenePhase < 30) { SearchToVanilla(me, false); return; }
        if (Player.WantedLevel > 1) { SearchToVanilla(me, false); return; }
        bool inVeh = veh != null && Exists(veh) && me.isInVehicle(veh);

        // keep the cuffed arms pinned behind his back while he is walked / driven
        // (every frame for the first second after it is applied, so it is never seen at any other frame)
        if (headPinned && headAnim != null && (now < pinFastUntil || now - lastPinAt > 300))
        {
            lastPinAt = now;
            if (headPhase > 0f) { try { GTA.Native.Function.Call("SET_CHAR_ANIM_CURRENT_TIME", me, headAnim[0], headAnim[1], headPhase); } catch { } }
            AnimSpeedOf(me, headAnim, 0f);
        }
        // walking / getting in: if anything knocked the cuffed upper-body pose off, it goes straight back on
        if (cuffPinned && !cuffsBroken && scenePhase >= 35 && scenePhase <= 73 && scenePhase != 37 && scenePhase != 39 && scenePhase != 40 && scenePhase != 71 && now - cuffCheckAt > 150)
        {
            cuffCheckAt = now;
            bool on = false;
            try { on = GTA.Native.Function.Call<bool>("IS_CHAR_PLAYING_ANIM", me, animCrimCuffed[0], animCrimCuffed[1]); } catch { on = true; }
            if (!on) cuffMiss++; else cuffMiss = 0;
            if (cuffMiss >= 2 && now - cuffReapplyAt > 1500)
            {
                cuffReapplyAt = now; cuffMiss = 0;
                CuffUpperBody(me, now);
                if (cuffReapplyLogs++ < 5) Log("Cuffed pose re-applied (it had been knocked off), phase " + scenePhase);
            }
        }
        if (cuffPinned && (now < pinFastUntil || now - lastPinAt > 400) && scenePhase >= 34 && scenePhase != 37)
        {
            lastPinAt = now;
            if (cuffPhase > 0f) { try { GTA.Native.Function.Call("SET_CHAR_ANIM_CURRENT_TIME", me, animCrimCuffed[0], animCrimCuffed[1], cuffPhase); } catch { } }
            AnimSpeedOf(me, animCrimCuffed, 0f);
        }

        // the partner: once at the passenger side of your car he checks under it (whatever the scene is doing
        // by then), then stands in the bouncer pose turned to you, watching you
        if (partnerWalkAt != 0 && !partnerUnder && Alive(searchPartner) && veh != null && Exists(veh) && scenePhase < 30)
        {
            Vector3 ps = CarSpot(veh, 0.9f, 0f);
            float dp = Dist2(searchPartner.Position, ps);
            bool pStopped = false;
            if (now - partnerCheckAt > 400) { pStopped = Dist2(searchPartner.Position, partnerLastPos) < 0.1f; partnerLastPos = searchPartner.Position; partnerCheckAt = now; }
            if (dp < 0.5f || (pStopped && dp < 1.5f) || now - partnerWalkAt > 15000)
            {
                partnerUnder = true; partnerUnderAt = now;
                try { searchPartner.Task.ClearAll(); } catch { }
                PlaceAt(searchPartner, ps, veh.Heading + 90f);
                PlayV(searchPartner, animCheckUnder, false);
            }
        }
        // the partner walking to the squad car: at the front passenger door he gets in
        if (partnerToCar && scenePhase >= 35 && Alive(searchPartner) && squadCar != null && Exists(squadCar))
        {
            Vector3 ps = SideDoorSpot(squadCar, false);
            float dp = Dist2(searchPartner.Position, ps);
            bool pStopped = false;
            if (now - partnerCheckAt > 400) { pStopped = Dist2(searchPartner.Position, partnerLastPos) < 0.1f; partnerLastPos = searchPartner.Position; partnerCheckAt = now; }
            if (dp < 0.8f || (pStopped && dp < 2.0f) || now - partnerWalkAt > 15000)
            {
                partnerToCar = false;
                try { searchPartner.Task.EnterVehicle(squadCar, VehicleSeat.RightFront); } catch { }
            }
            else if (pStopped && dp >= 2.0f && now - partnerWalkAt > 1500) WalkStraight(searchPartner, ps);
        }
        if (partnerUnder && !partnerLook && Alive(searchPartner) && now - partnerUnderAt > 900)
        {
            bool still = false;
            try { still = GTA.Native.Function.Call<bool>("IS_CHAR_PLAYING_ANIM", searchPartner, animCheckUnder[0], animCheckUnder[1]); } catch { }
            if (!still || now - partnerUnderAt > 15000)
            {
                partnerLook = true;
                try
                {
                    searchPartner.Heading = HeadingTo(searchPartner.Position, me.Position);    // turned to you
                    GTA.Native.Function.Call("TASK_LOOK_AT_CHAR", searchPartner, me, 600000, 0);
                }
                catch { }
                PlayV(searchPartner, animPartnerLook, true);
            }
        }

        switch (scenePhase)
        {
            // ---- "get out / off and open the trunk": LB refuse, RB comply
            case 0:
                // his hand stays locked on the car while he talks (as at a StopRightThere ticket)
                if (!searchBike)
                {
                    if (!windowPoseHeld && windowPoseAt != 0 && now >= windowPoseAt)
                    {
                        windowPoseHeld = true;
                        windowPoseTime = AnimPhase(cop, animCopCarChat);
                        AnimSpeedOf(cop, animCopCarChat, 0f);
                    }
                    else if (windowPoseHeld)
                    {
                        try { GTA.Native.Function.Call("BLOCK_CHAR_GESTURE_ANIMS", cop, true); } catch { }
                        if (!AnimPlaying(cop, animCopCarChat)) AnimLoop(cop, animCopCarChat);
                        if (windowPoseTime > 0f) { try { GTA.Native.Function.Call("SET_CHAR_ANIM_CURRENT_TIME", cop, animCopCarChat[0], animCopCarChat[1], windowPoseTime); } catch { } }
                        AnimSpeedOf(cop, animCopCarChat, 0f);
                    }
                }
                if (inVeh)
                {
                    bool drove = false;
                    try { drove = veh.Speed > 3f; } catch { }
                    if (drove) { EndSearch(4); return; }
                }
                else if (!searchBike) { EndSearch(4); return; }
                if (btn[B_ROB].Pressed || t > 20000)
                {
                    choiceKind = 0; copChoice = false;
                    ReleaseWindowPose(cop);
                    PlayPlayer("SearchRefuse");
                    try { cop.Task.ClearAll(); cop.Task.TurnTo(me); } catch { }
                    NextPhase(90, now);
                }
                else if (btn[B_COMPLY].Pressed && t > 300)
                {
                    choiceKind = 0; copChoice = false;
                    ReleaseWindowPose(cop);
                    PlayPlayer("Search");
                    LockPlayer(true);
                    if (searchBike) { try { me.Task.LeaveVehicle(veh, false); } catch { } NextPhase(60, now); }
                    else
                    {
                        // you get out right away while the officer steps back from the door
                        try { me.Task.LeaveVehicle(veh, true); } catch { }
                        try { cop.Task.ClearAll(); } catch { }
                        WalkStraight(cop, CarSpot(veh, -1.4f, -0.3f));
                        NextPhase(2, now);
                    }
                }
                break;

            // ---- refused: the line plays, 1.5 s later the game's own 1 star; they try to arrest you
            case 90:
                if (t >= 1500) SearchToVanilla(me, true);
                break;

            // ---- car: out, to the trunk
            case 1:
                if (t >= 1100) { try { me.Task.LeaveVehicle(veh, true); } catch { } NextPhase(2, now); }
                break;
            case 2:
                if (inVeh)
                {
                    if (t > 4000) { try { me.Task.LeaveVehicleImmediately(veh); } catch { } }
                    break;
                }
                if (outAt == 0) { outAt = now; break; }
                if (now - outAt < 350) break;                           // the get-out animation finishes first
                searchSpot = CarSpot(veh, 0f, -0.8f);                   // behind the trunk
                asideSpot = CarSpot(veh, 0.55f, -1.3f);                // just off the rear corner
                try { me.Task.ClearAll(); } catch { }
                WalkThrough(me, searchSpot);
                walkCheckAt = now; walkTries = 0; lastWalkPos = me.Position;
                WalkStraight(cop, CarSpot(veh, -0.6f, -0.9f));          // the officer walks round with you
                if (Alive(searchPartner))
                {
                    try { searchPartner.Task.ClearAll(); } catch { }
                    WalkThrough(searchPartner, CarSpot(veh, 0.9f, 0f));     // walks to the passenger side, middle of the car
                    partnerWalkAt = now; partnerLastPos = searchPartner.Position; partnerCheckAt = now;
                }
                NextPhase(3, now);
                break;
            case 3:
                {
                    // he walks through the spot (the walk task alone stops short of it); the moment he is on it,
                    // or has stopped close to it, the trunk opens - no standing around
                    float dTrunk = Dist2(me.Position, searchSpot);
                    bool stopped = false;
                    if (now - walkCheckAt > 400)
                    {
                        stopped = Dist2(me.Position, lastWalkPos) < 0.12f;
                        if (stopped && dTrunk >= 1.3f) { WalkThrough(me, searchSpot); Log("Trunk walk re-ordered"); }
                        walkCheckAt = now; lastWalkPos = me.Position;
                    }
                    if (!(dTrunk < 0.45f || (stopped && dTrunk < 1.3f) || t > 5000)) break;
                }
                {
                    try { me.Task.ClearAll(); } catch { }
                    if (me.Position.DistanceTo(searchSpot) > 2.4f)
                    {
                        Vector3 cur = me.Position;
                        try { GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", me, searchSpot.X, searchSpot.Y, cur.Z); } catch { }
                    }
                    PlaceAt(me, searchSpot, veh.Heading);
                    PlayV(me, animPlayerOpenTrunk, false);
                    NextPhase(4, now);
                }
                break;
            case 4:
                if (!sceneFlag && t >= 700) { sceneFlag = true; try { veh.Door(VehicleDoor.Trunk).Open(); } catch { } }
                if (t >= 1500)
                {
                    // you step aside straight away (the trunk animation is cut so the order is not ignored)
                    try { me.Task.ClearAll(); } catch { }
                    WalkThrough(me, asideSpot);
                    try { cop.Task.ClearAll(); } catch { }
                    WalkThrough(cop, searchSpot);
                    walkCheckAt = now; lastWalkPos = cop.Position;
                    NextPhase(5, now);
                }
                break;
            case 5:
                {
                    bool copStopped = false;
                    if (now - walkCheckAt > 400)
                    {
                        copStopped = Dist2(cop.Position, lastWalkPos) < 0.12f;
                        walkCheckAt = now; lastWalkPos = cop.Position;
                    }
                    if (Dist2(me.Position, asideSpot) < 0.4f) { try { me.Task.ClearAll(); } catch { } }
                    float dc = Dist2(cop.Position, searchSpot);
                    if (!(dc < 0.45f || (copStopped && dc < 1.3f) || t > 3000)) break;
                }
                {
                    PlaceAt(cop, searchSpot, veh.Heading);
                    PlayV(cop, animTrunkSearch, true);
                    try { me.Task.ClearAll(); me.Task.TurnTo(cop); } catch { }
                    NextPhase(6, now);
                }
                break;
            case 6:     // he goes through the trunk; 5 s in, the roll
                if (t >= searchMs)
                {
                    searchFound = Carried() > 0 && rnd.Next(100) < searchFindChance;
                    PlayV(cop, animCloseBoot, false);
                    NextPhase(7, now);
                }
                break;
            case 7:
                if (!sceneFlag && t >= 600) { sceneFlag = true; try { veh.Door(VehicleDoor.Trunk).Close(); } catch { } }
                if (t >= 1500)
                {
                    try { cop.Task.ClearAll(); cop.Task.TurnTo(me); } catch { }
                    if (searchFound)
                    {
                        // the drugs were in the car: they are gone whatever you do now
                        stash[0] = 0; stash[1] = 0; stash[2] = 0;
                        SaveStash();
                        NextPhase(80, now);
                    }
                    else
                    {
                        CopSay(cop, "FOUND_NOTHING", 2);
                        if (Alive(searchPartner)) { try { searchPartner.Task.ClearAll(); } catch { } }
                        WalkStraight(me, CarSpot(veh, -0.7f, 0.3f));        // back to the driver's door
                        WalkStraight(cop, CarSpot(veh, -1.4f, -0.5f));
                        NextPhase(20, now);
                    }
                }
                break;

            // ---- found: he turns to you, a beat, the arrest line; your hands go up the way an NPC's do
            // at gunpoint (the game's own hands-up), then flow straight into armsup_loop - never dropped
            case 80:
                if (t >= 800) { SayArrest(cop); NextPhase(81, now); }
                break;
            case 81:
                if (t >= 1000)
                {
                    try { me.Task.HandsUp(60000); } catch (Exception ex) { Log("Hands up: " + ex.Message); }
                    NextPhase(82, now);
                }
                break;
            case 82:    // the hands are up: armsup_loop blends in from there (no clear in between)
                if (t >= handsUpToLoopMs)
                {
                    PlayV(me, animArmsUp, true);
                    choiceKind = 2; copChoice = true;
                    NextPhase(8, now);
                }
                break;

            // ---- found nothing: back in (or on), the ticket script carries on
            case 20:
                if (me.Position.DistanceTo(CarSpot(veh, -0.7f, 0.3f)) < 0.7f || t > 9000)
                {
                    try { me.Task.EnterVehicle(veh, VehicleSeat.Driver); } catch { }
                    NextPhase(21, now);
                }
                break;
            case 21:
                if (inVeh || t > 9000)
                {
                    if (!inVeh) { try { me.WarpIntoVehicle(veh, VehicleSeat.Driver); } catch { } }
                    EndSearch(2);
                }
                break;

            // ---- found: lock on (LT), D-pad left comply / right refuse
            case 8:
                if (!aimHeld && t < 20000) break;
                if (btn[B_D0 + PILLS].Pressed || t > 20000)
                {
                    choiceKind = 0; copChoice = false;
                    try { me.Task.ClearAll(); } catch { }
                    PlayPlayer("SearchRefuse");
                    LockPlayer(false);                       // free to run while the line plays
                    NextPhase(90, now);
                }
                else if (btn[B_D0 + HEROIN].Pressed && t > 300)
                {
                    choiceKind = 0; copChoice = false;
                    PlayPlayer("SearchComply");
                    if (!searchBike) { PlayV(me, animArmsToSearched, false); NextPhase(31, now); }
                    else { StartCuffApproach(me, cop, now); NextPhase(32, now); }
                }
                break;

            // ---- complied: searched pose, cuffed from behind
            case 31:    // hands go to the head; frozen the moment they are there
                if (HandsOnHead(me, t))
                {
                    // the pose moves onto the upper body (held on the same frame), so the legs can turn you round
                    HeadUpperBody(me, now);
                    Vector3 mp = me.Position, cp = cop.Position;
                    turnTarget = new Vector3(mp.X + (mp.X - cp.X), mp.Y + (mp.Y - cp.Y), mp.Z);   // straight away from him
                    try { me.Task.TurnTo(turnTarget); } catch { }
                    NextPhase(41, now);
                }
                break;
            case 41:    // you turn your back to him, hands still on your head; he walks up behind you
                {
                    float want = HeadingTo(me.Position, turnTarget);
                    float diff = Math.Abs(((me.Heading - want) % 360f + 540f) % 360f - 180f);
                    if (diff < 12f || t > 2500)
                    {
                        me.Heading = want;
                        cuffLeg = 1; walkCheckAt = now;
                        try { cop.Task.ClearAll(); } catch { }
                        WalkThrough(cop, CuffApproachSpot(me));
                        NextPhase(32, now);
                    }
                }
                break;
            case 32:    // he steps in behind you
                {
                    Vector3 behind = CuffApproachSpot(me);
                    if (cuffLeg == 0 && (cop.Position.DistanceTo(cuffSide) < 0.5f || t > 3500))
                    {
                        cuffLeg = 1; walkCheckAt = now;
                        WalkThrough(cop, behind);
                    }
                    else if (cuffLeg == 1 && (Dist2(cop.Position, behind) < 0.15f || now - walkCheckAt > 3000))
                    {
                        // he stops one footstep behind you; only a small correction if he is a little off
                        try { cop.Task.ClearAll(); } catch { }
                        if (Dist2(cop.Position, behind) < 0.6f) PlaceAt(cop, behind, me.Heading);
                        else cop.Heading = me.Heading;
                        NextPhase(33, now);
                    }
                }
                break;
            case 33:    // the cuffing: crim_cuffed and cop_cuff are a matched pair, made to be played from ONE spot.
                        // Both start on the same frame with the officer set on exactly your spot and heading; the pair
                        // itself then puts his body behind you with his hands on yours. His feet are held there
                        // (no collision with you, so neither of you is pushed or slides) until the cuffs are on.
                if (t < 150) break;
                headPinned = false;
                try { me.Task.ClearSecondary(); } catch { }
                try { cop.Task.ClearAllImmediately(); } catch { }
                try
                {
                    Vector3 mp = me.Position;
                    cuffZ = cop.Position.Z;
                    GTA.Native.Function.Call("SET_CHAR_COLLISION", cop, false);
                    GTA.Native.Function.Call("FREEZE_CHAR_POSITION", cop, false);
                    GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, mp.X, mp.Y, mp.Z);
                    cop.Heading = me.Heading;
                    GTA.Native.Function.Call("FREEZE_CHAR_POSITION", cop, true);
                    copPinned = true;
                }
                catch (Exception ex) { Log("Cuff placement: " + ex.Message); }
                PlayV(me, animCrimCuffed, false);
                PlayV(cop, animCopCuff, false);
                Log("Cuffing: both animations started from your spot");
                cuffComp = 0; cuffEnded = false; cuffLogAt = 0;
                NextPhase(34, now);
                break;
            case 34:    // frame 165 of crim_cuffed: hands behind his back, held there
                {
                    float cph = AnimPhase(cop, animCopCuff);
                    if (cph <= 0.001f || cph > 1.01f) cph = t / 6000f;
                    // where his body starts the pair, measured once, so next time he walks up to exactly there
                    if (cuffComp == 0 && cph >= 0.06f && t >= 300)
                    {
                        cuffComp = 1;
                        try
                        {
                            Vector3 h = Hips(cop), mp = me.Position;
                            Vector3 f = Fwd(me.Heading), r = Rgt(me.Heading);
                            float dx = h.X - mp.X, dy = h.Y - mp.Y;
                            cuffOffF = dx * f.X + dy * f.Y; cuffOffR = dx * r.X + dy * r.Y; cuffOffKnown = true;
                            Log("cop_cuff starts his body " + S(cuffOffF) + " m forward, " + S(cuffOffR) + " m right of you");
                        }
                        catch { }
                    }
                    if (now - cuffLogAt > 1000)
                    {
                        cuffLogAt = now;
                        try
                        {
                            Vector3 mL = me.GetBonePosition(Bone.LeftHand), mR = me.GetBonePosition(Bone.RightHand);
                            Vector3 cL = cop.GetBonePosition(Bone.LeftHand), cR = cop.GetBonePosition(Bone.RightHand);
                            float hx = (mL.X + mR.X - cL.X - cR.X) * 0.5f, hy = (mL.Y + mR.Y - cL.Y - cR.Y) * 0.5f;
                            Log("Cuffing: his hands " + S((float)Math.Sqrt(hx * hx + hy * hy)) + " m from yours");
                        }
                        catch { }
                    }
                    // the end of cop_cuff: stopped just before it finishes, he is left standing where his body is
                    // (his feet are under you until then), collision back on
                    if (!cuffEnded && (cph >= 0.95f || (t > 800 && !AnimPlaying(cop, animCopCuff))))
                    {
                        cuffEnded = true;
                        Vector3 ch = Hips(cop);
                        try { cop.Task.ClearAllImmediately(); } catch { }
                        PinCop(cop, false);
                        try { GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, ch.X, ch.Y, cuffZ); cop.Heading = me.Heading; } catch { }
                    }
                }
                if (t >= cuffHoldMs || (t > 600 && !AnimPlaying(me, animCrimCuffed)))
                {
                    Log("Cuffed: officer's hips " + S(Dist2(Hips(cop), Hips(me))) + " m from yours");
                    if (!cuffEnded)
                    {
                        cuffEnded = true;
                        Vector3 ch = Hips(cop);
                        try { cop.Task.ClearAllImmediately(); } catch { }
                        PinCop(cop, false);
                        try { GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, ch.X, ch.Y, cuffZ); cop.Heading = me.Heading; } catch { }
                    }
                    if (copPinned) PinCop(cop, false);
                    cuffPhase = AnimPhase(me, animCrimCuffed);
                    AnimSpeedOf(me, animCrimCuffed, 0f);      // held on the hands-behind-the-back frame
                    NextPhase(35, now);
                }
                break;
            case 35:    // the walk to the squad car: arms behind his back, legs free, the officer at your side
                if (t >= 300)
                {
                    if (squadCar == null || !Exists(squadCar)) squadCar = FindSquadCar(cop);
                    if (squadCar == null) { Log("No squad car to put you in"); NextPhase(40, now); break; }
                    squadSeat = VehicleSeat.LeftRear;
                    try { if (!squadCar.isSeatFree(VehicleSeat.LeftRear)) squadSeat = VehicleSeat.RightRear; } catch { }
                    // you wait beside the front door (clear of the rear door's swing); he holds the rear door
                    doorSpot = WaitBySquadDoor(squadCar, squadSeat);
                    copDoorSpot = CopDoorSpot(squadCar, squadSeat);
                    copClearSpot = CopClearSpot(squadCar, squadSeat);
                    copHandleSpot = CopHandleSpot(squadCar, squadSeat);
                    escortTarget = new Vector3(0f, 0f, 0f);
                    partnerToCar = false; partnerEntering = false; copToDriver = false; copHandleTries = 0; copToHandleAt = 0;
                    rearOpenAngle = 0f; doorClosing = false; copTurned = false; sitStart = 0; sitZ = 0f; doorOpenStart = 0;
                    // you stay standing still, cuffed; first he steps out of your way, to your side of the walk to
                    // the squad car, so you never walk into him (that is what set off the game's push-past arm)
                    try
                    {
                        Vector3 mp = me.Position;
                        float wx = doorSpot.X - mp.X, wy = doorSpot.Y - mp.Y, wl = (float)Math.Sqrt(wx * wx + wy * wy);
                        if (wl < 0.1f) { Vector3 bf = BodyForward(me); wx = bf.X; wy = bf.Y; wl = 1f; }
                        wx /= wl; wy /= wl;
                        Vector3 rel = cop.Position - mp;
                        escortSide = (rel.X * wy - rel.Y * wx) >= 0f ? 1f : -1f;
                        // right of the walk is (wy, -wx)
                        copSideSpot = new Vector3(mp.X + wy * escortSide * 1.3f - wx * 0.3f, mp.Y - wx * escortSide * 1.3f - wy * 0.3f, mp.Z);
                    }
                    catch { escortSide = 1f; copSideSpot = cop.Position; }
                    try { cop.Task.ClearAll(); } catch { }
                    WalkStraight(cop, copSideSpot);
                    copCheckAt = now; lastCopPos = cop.Position;
                    NextPhase(42, now);
                }
                break;
            case 42:    // he is out of your way: now you walk to the squad car, hands cuffed, him at your side
                {
                    bool st = false;
                    if (now - copCheckAt > 400) { st = Dist2(cop.Position, lastCopPos) < 0.1f; copCheckAt = now; lastCopPos = cop.Position; }
                    float dc = Dist2(cop.Position, copSideSpot);
                    if (!(dc < 0.4f || (st && dc < 1.2f) || t > 3500)) break;
                    // the held full-body pose goes and, in the same frame, the cuffed pose goes on the upper body
                    // (held on the hands-behind-the-back frame): legs walk, hands stay cuffed
                    try { me.Task.ClearAllImmediately(); } catch { }
                    CuffUpperBody(me, now);
                    WalkPath(me, doorSpot);
                    walkCheckAt = now; walkTries = 0; lastWalkPos = me.Position;
                    // he goes straight for the rear door on your side, at a quick walk, ahead of you
                    copAtDoor = true; escortAt = now; copToHandleAt = now;
                    CopStraightToDoor(cop);
                    lastCopPos = cop.Position; copCheckAt = now;
                    // the partner WALKS to the front passenger door, then gets in
                    if (Alive(searchPartner)) { try { searchPartner.Task.ClearAllImmediately(); } catch { } WalkPath(searchPartner, SideDoorSpot(squadCar, false)); partnerWalkAt = now; partnerCheckAt = now; partnerLastPos = searchPartner.Position; partnerToCar = true; }
                    NextPhase(36, now);
                }
                break;
            case 36:    // you walk to the squad car; he goes straight for the door ahead of you
                {
                    float left = Dist2(me.Position, doorSpot);
                    if (copViaWaypoint && Dist2(cop.Position, copWaypoint) < 0.8f) { copViaWaypoint = false; WalkStraight(cop, copHandleSpot); }
                    // close to the car he goes ahead and opens the rear door himself (the game's own
                    // open-door animation: he is sent to that seat and stopped before he gets in)
                    if (!copAtDoor && left < 4.0f)
                    {
                        copAtDoor = true; escortAt = now; copToHandleAt = now;
                        CopStraightToDoor(cop);
                        lastCopPos = cop.Position; copCheckAt = now;
                    }
                    else if (!copAtDoor && now - escortAt > 400) Escort(cop, me, now);
                    bool stopped = false;
                    if (now - walkCheckAt > 500)
                    {
                        stopped = Dist2(me.Position, lastWalkPos) < 0.12f;
                        if (stopped && left >= 1.5f)
                        {
                            walkTries++;
                            if (walkTries >= 2) { try { me.Task.ClearAllImmediately(); } catch { } CuffUpperBody(me, now); }
                            WalkThrough(me, doorSpot);
                            Log("Squad car walk re-ordered (" + walkTries + ")");
                        }
                        walkCheckAt = now; lastWalkPos = me.Position;
                    }
                    if (left < 0.5f || (stopped && left < 1.5f) || t > 25000)
                    {
                        // you wait out here, clear of the door, facing it
                        try { me.Task.TurnTo(RearDoorCentre(squadCar, squadSeat)); } catch { }
                        if (!copAtDoor) { copAtDoor = true; escortAt = now; copToHandleAt = now; CopStraightToDoor(cop); lastCopPos = cop.Position; copCheckAt = now; }
                        doorOpenStart = 0;
                        NextPhase(70, now);
                    }
                }
                break;
            case 70:    // he reaches the door and opens it with his own hands (the door follows his animation),
                        // then gets about 3 m clear of it. He never gets in.
                {
                    VehicleDoor rear = squadSeat == VehicleSeat.LeftRear ? VehicleDoor.LeftRear : VehicleDoor.RightRear;
                    if (doorOpenStart == 0)
                    {
                        // the door is only opened once he is standing right at it (by the handle). He walks there
                        // round the car; if he is not moving the order is given again (path, then straight);
                        // if he still is not there after 25 s he is put there.
                        float dh = Dist2(cop.Position, copHandleSpot);
                        bool there = dh < 0.35f;
                        if (copViaWaypoint && Dist2(cop.Position, copWaypoint) < 0.8f) { copViaWaypoint = false; WalkStraight(cop, copHandleSpot); }
                        if (now - copCheckAt > 600)
                        {
                            bool stuckNow = Dist2(cop.Position, lastCopPos) < 0.1f;
                            if (stuckNow && dh < 0.9f) there = true;                      // as close as he can get
                            else if (stuckNow) { copHandleTries++; copViaWaypoint = false; WalkStraight(cop, copHandleSpot); }
                            copCheckAt = now; lastCopPos = cop.Position;
                        }
                        if (!there && now - copToHandleAt > 25000)
                        {
                            try { cop.Task.ClearAllImmediately(); } catch { }
                            try { Vector3 c = cop.Position; GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, copHandleSpot.X, copHandleSpot.Y, c.Z); } catch { }
                            Log("Officer was not at the rear door after 25 s: put there");
                            there = true;
                        }
                        if (!there) break;
                        WalkPace(cop, 1f);
                        try { cop.Task.ClearAll(); } catch { }
                        if (Dist2(cop.Position, copHandleSpot) < 0.9f) { try { Vector3 c = cop.Position; GTA.Native.Function.Call("SET_CHAR_COORDINATES_NO_OFFSET", cop, copHandleSpot.X, copHandleSpot.Y, c.Z); } catch { } }
                        try { cop.Heading = HeadingTo(copHandleSpot, FacingCar(squadCar, copHandleSpot)); } catch { }
                        PlayV(cop, animCopOpenDoor, false);
                        doorOpenStart = now;
                        break;
                    }
                    if (!rearDoorOpened)
                    {
                        // the door swings out with his arm (time based, about 0.3 s to 1.1 s into his animation)
                        float k = (now - doorOpenStart - 300) / 800f;
                        if (k < 0f) k = 0f;
                        if (k > 1f) k = 1f;
                        try { GTA.Native.Function.Call("CONTROL_CAR_DOOR", squadCar, (int)rear, 1, k); } catch { }
                        try { squadCar.Door(rear).Angle = 1.0f * k; } catch { }
                        if (k >= 1f)
                        {
                            rearDoorOpened = true; rearOpenAngle = 1f;
                            // a step out of your way, then he stands still and watches you
                            try { cop.Task.ClearAll(); } catch { }
                            WalkStraight(cop, copClearSpot);
                            escortAt = now; lastCopPos = cop.Position; copCheckAt = now;
                        }
                        break;
                    }
                    HoldRearOpen(rear);
                    CopStandAside(cop, me, now);
                    // you stay where you are until he is out of the doorway (well clear of the door handle), so
                    // you never walk into him
                    if (Dist2(cop.Position, copHandleSpot) > 1.6f || copTurned || now - escortAt > 3500)
                    {
                        // the doorway is clear: you WALK to it (hands still behind your back)
                        WalkThrough(me, DoorGapSpot(squadCar, squadSeat));
                        walkCheckAt = now; lastWalkPos = me.Position;
                        NextPhase(71, now);
                    }
                }
                break;
            case 71:    // the game's own get-in. The hands-behind-the-back pose is already on from the walk and is
                        // NOT given again (giving it again is what cut the get-in short before). The door is held
                        // open by the script, and the get-in is stopped the moment you are sat down, before the
                        // reach for the door.
                {
                    VehicleDoor rear = squadSeat == VehicleSeat.LeftRear ? VehicleDoor.LeftRear : VehicleDoor.RightRear;
                    HoldRearOpen(rear);
                    CopStandAside(cop, me, now);
                    Vector3 gap = DoorGapSpot(squadCar, squadSeat);
                    if (sitStart == 0)
                    {
                        float dg = Dist2(me.Position, gap);
                        bool stopped = false;
                        if (now - walkCheckAt > 400) { stopped = Dist2(me.Position, lastWalkPos) < 0.1f; walkCheckAt = now; lastWalkPos = me.Position; if (stopped && dg >= 0.9f) WalkThrough(me, gap); }
                        if (!(dg < 0.6f || (stopped && dg < 1.2f) || t > 12000)) break;
                        try { me.Task.EnterVehicle(squadCar, squadSeat); } catch { }
                        sitStart = now; inCarAt = 0; getInSeen = false;
                        Log("Get-in: the game's own get-in, arms kept from the walk");
                        break;
                    }
                    // arms: only held on their frame (no new order)
                    if (cuffPhase > 0f) { try { GTA.Native.Function.Call("SET_CHAR_ANIM_CURRENT_TIME", me, animCrimCuffed[0], animCrimCuffed[1], cuffPhase); } catch { } }
                    AnimSpeedOf(me, animCrimCuffed, 0f);
                    // sat down = the get-in animation has played to its end (it is followed by the door pull)
                    bool sat = false;
                    bool gi = false;
                    try { gi = GTA.Native.Function.Call<bool>("IS_CHAR_PLAYING_ANIM", me, "veh@std", "get_in_ds"); } catch { }
                    if (gi)
                    {
                        getInSeen = true;
                        float gph = -1f;
                        try { AnimationSet vs; if (!animSets.TryGetValue("veh@std", out vs)) { vs = new AnimationSet("veh@std"); animSets["veh@std"] = vs; } gph = me.Animation.GetCurrentAnimationTime(vs, "get_in_ds"); } catch { }
                        if (gph >= 0.93f && gph <= 1.01f) sat = true;
                    }
                    else if (getInSeen) sat = true;
                    if (me.isInVehicle(squadCar))
                    {
                        if (inCarAt == 0) inCarAt = now;
                        if (!getInSeen && now - inCarAt >= 1100) sat = true;
                    }
                    if (!sat)
                    {
                        if (now - sitStart > 9000 && !sceneFlag) { sceneFlag = true; try { me.Task.EnterVehicle(squadCar, squadSeat); } catch { } Log("Get-in re-ordered"); }
                        if (now - sitStart > 20000) { try { me.WarpIntoVehicle(squadCar, squadSeat); } catch { } Log("Get-in failed for 20 s: put in the seat"); sat = true; }
                        if (!sat) break;
                    }
                    // sat down: the get-in is stopped here, so you never pull the door
                    try { me.Task.ClearAll(); } catch { }
                    if (!me.isInVehicle(squadCar)) { try { me.WarpIntoVehicle(squadCar, squadSeat); } catch { } }
                    CuffUpperBody(me, now);
                    try { squadCar.SirenActive = false; } catch { }
                    meInAt = now;
                    Log("Seated after " + (now - sitStart) + " ms (get-in seen: " + getInSeen + "); door left open for the officer");
                    doorClosing = true; escortAt = now;
                    try { cop.Task.ClearAll(); } catch { }
                    WalkStraight(cop, copDoorSpot);
                    walkCheckAt = now; lastCopPos = cop.Position;
                    NextPhase(73, now);
                }
                break;
            case 73:    // he walks back to the door (it stays open) and shuts it with d_close_out_ds
                {
                    VehicleDoor rear = squadSeat == VehicleSeat.LeftRear ? VehicleDoor.LeftRear : VehicleDoor.RightRear;
                    HoldRearOpen(rear);
                    bool stopped = false;
                    if (now - walkCheckAt > 400) { stopped = Dist2(cop.Position, lastCopPos) < 0.1f; walkCheckAt = now; lastCopPos = cop.Position; }
                    float dd = Dist2(cop.Position, copDoorSpot);
                    if (dd < 0.3f || (stopped && dd < 1.0f) || now - escortAt > 6000)
                    {
                        try { doorStartAngle = squadCar.Door(rear).Angle; } catch { doorStartAngle = 1f; }
                        if (doorStartAngle < 0.1f) doorStartAngle = 1f;
                        try { cop.Task.ClearAll(); } catch { }
                        PlaceAt(cop, copDoorSpot, HeadingTo(copDoorSpot, FacingCar(squadCar, copDoorSpot)));
                        PlayV(cop, animCopCloseDoor, false);
                        NextPhase(72, now);
                    }
                }
                break;
            case 72:    // the door follows his close-door animation, then he goes to the driver's seat
                {
                    VehicleDoor rear = squadSeat == VehicleSeat.LeftRear ? VehicleDoor.LeftRear : VehicleDoor.RightRear;
                    float ph = AnimPhase(cop, animCopCloseDoor);
                    // the door moves from about 20% to 65% of the animation (time based if the game won't say)
                    float k = ph > 0.01f && ph <= 1.01f ? (ph - 0.2f) / 0.45f : (t - 250) / 800f;
                    if (k < 0f) k = 0f;
                    if (k > 1f) k = 1f;
                    try
                    {
                        if (k >= 1f) { GTA.Native.Function.Call("CONTROL_CAR_DOOR", squadCar, (int)rear, 0, 0f); squadCar.Door(rear).Close(); rearDoorClosed = true; }
                        else
                        {
                            float a = doorStartAngle * (1f - k);
                            GTA.Native.Function.Call("CONTROL_CAR_DOOR", squadCar, (int)rear, 1, a);
                            squadCar.Door(rear).Angle = a;
                        }
                    }
                    catch { CloseRear(rear); rearDoorClosed = true; }
                    if (rearDoorClosed && (t > 600 && (!AnimPlaying(cop, animCopCloseDoor) || t > 2500)))
                    {
                        // he WALKS round to the driver's door, then gets in
                        try { cop.Task.ClearAll(); } catch { }
                        WalkPath(cop, SideDoorSpot(squadCar, true));
                        copToDriver = true; copDriverAt = now; copCheckAt = now; lastCopPos = cop.Position;
                        NextPhase(37, now);
                    }
                }
                break;
            case 37:    // the officers get in; then the ride
                {
                    VehicleDoor rear = squadSeat == VehicleSeat.LeftRear ? VehicleDoor.LeftRear : VehicleDoor.RightRear;
                    if (copToDriver)
                    {
                        Vector3 ds = SideDoorSpot(squadCar, true);
                        bool st = false;
                        if (now - copCheckAt > 400) { st = Dist2(cop.Position, lastCopPos) < 0.1f; copCheckAt = now; lastCopPos = cop.Position; }
                        float dd = Dist2(cop.Position, ds);
                        if (dd < 0.8f || (st && dd < 2.0f) || now - copDriverAt > 10000)
                        {
                            copToDriver = false;
                            try { cop.Task.EnterVehicle(squadCar, VehicleSeat.Driver); } catch { }
                        }
                    }
                    bool copIn = cop.isInVehicle(squadCar);
                    bool partnerIn = !Alive(searchPartner) || searchPartner.isInVehicle(squadCar);
                    if (now - doorCheckAt > 1000) { doorCheckAt = now; CloseRear(rear); }
                    if ((copIn && partnerIn) || t > 15000)
                    {
                        try
                        {
                            if (!me.isInVehicle(squadCar)) me.WarpIntoVehicle(squadCar, squadSeat);
                            if (!copIn) cop.WarpIntoVehicle(squadCar, VehicleSeat.Driver);
                            if (!partnerIn) searchPartner.WarpIntoVehicle(squadCar, VehicleSeat.RightFront);
                        }
                        catch { }
                        CloseRear(rear);
                        rearDoorClosed = true;
                        // the ride: lights and siren, to the nearest police HQ spot. Parked nose-up to a car,
                        // he first backs up enough to steer round it.
                        hqKnown = NearestHq(squadCar.Position, out hqTarget);
                        try { squadCar.SirenActive = false; } catch { }      // no siren with you in the back
                        rideStart = now; doorCheckAt = now; stuckSince = 0;
                        if (BlockedAhead()) { reverseUntil = now + 1400; try { cop.Task.ClearAll(); } catch { } Log("Squad car: backing up to get round the car in front"); }
                        else { reverseUntil = 0; DriveToHq(cop, now); }
                        NextPhase(38, now);
                    }
                }
                break;
            case 38:
                {
                    // pick the cuffs (RB 10 times, fast), then Y bails out like jumping from your own car
                    if (!cuffsBroken && now - rideStart >= lockpickAfterMs) { choiceKind = 3; copChoice = true; }
                    if (choiceKind == 3 && btn[B_COMPLY].Pressed)
                    {
                        if (lastPickAt != 0 && now - lastPickAt <= lockpickWindowMs) pickCount++;
                        else pickCount = 1;
                        lastPickAt = now;
                        if (pickCount >= lockpickPresses)
                        {
                            cuffsBroken = true; cuffPinned = false;
                            choiceKind = 0; copChoice = false;
                            try { me.Task.ClearSecondary(); } catch { }
                            LockPlayer(false);                  // Y now gets you out, like any moving car
                            Log("Cuffs picked");
                        }
                    }
                    if (choiceKind == 3 && lastPickAt != 0 && now - lastPickAt > lockpickWindowMs) pickCount = 0;
                    if (cuffsBroken)
                    {
                        if (!me.isInVehicle()) { SearchToVanilla(me, false); return; }
                        if ((padButtons & PAD_Y) != 0 && yAt == 0) yAt = now;
                        if (yAt != 0 && now - yAt > 700 && me.isInVehicle())
                        {
                            try { GTA.Native.Function.Call("TASK_LEAVE_CAR_IMMEDIATELY", me, squadCar); } catch { }
                            yAt = 0;
                        }
                    }
                    if (!Alive(cop) || squadCar == null || !Exists(squadCar)) { SearchToVanilla(me, false); return; }
                    if (!cuffsBroken && now - doorCheckAt > 1000)
                    {
                        doorCheckAt = now;
                        CloseRear(squadSeat == VehicleSeat.LeftRear ? VehicleDoor.LeftRear : VehicleDoor.RightRear);
                    }
                    // backing up (only when something blocks his nose), then ONE drive order; it is only given
                    // again if he is stuck, after backing up again (re-giving it all the time made him dither)
                    float spd = 0f;
                    try { spd = squadCar.Speed; } catch { }
                    if (reverseUntil != 0)
                    {
                        if (now < reverseUntil) { try { GTA.Native.Function.Call("SET_CAR_FORWARD_SPEED", squadCar, -3.0f); } catch { } }
                        else { reverseUntil = 0; stuckSince = 0; DriveToHq(cop, now); }
                    }
                    else
                    {
                        if (spd > 1.5f) stuckSince = 0;
                        else if (stuckSince == 0) stuckSince = now;
                        else if (now - stuckSince > 3500 && now - lastDriveOrder > 3500)
                        {
                            stuckSince = 0;
                            if (BlockedAhead()) { reverseUntil = now + 1400; try { cop.Task.ClearAll(); } catch { } Log("Squad car stuck: backing up"); }
                            else { DriveToHq(cop, now); Log("Squad car stuck: drive order again"); }
                        }
                    }
                    float left = hqKnown ? squadCar.Position.DistanceTo(hqTarget) : 9999f;
                    // he starts braking early enough to roll to a stop at the spot (gentle, even braking)
                    float brakeDist = Math.Max(8f, spd * spd / (2f * HqBrake) + 3f);
                    if ((hqKnown && left < brakeDist) || (!hqKnown && now - rideStart > 90000)) { brakeV0 = -1f; stoppedAt = 0; NextPhase(39, now); }
                }
                break;
            case 39:    // pull in at the spot: the car slows down evenly and stops; only then does everyone get out
                {
                    float sp = 0f;
                    try { sp = squadCar.Speed; } catch { }
                    if (brakeV0 < 0f) brakeV0 = Math.Max(sp, 0.1f);
                    // the speed allowed now goes down by HqBrake m/s every second (he keeps steering)
                    float cap = brakeV0 - HqBrake * (t / 1000f);
                    if (cap < 0f) cap = 0f;
                    if (sp > cap) { try { squadCar.Speed = cap; } catch { } }
                    if (stoppedAt == 0 && (cap <= 0f || sp < 0.3f) && (sp < 0.3f || t > 12000))
                    {
                        stoppedAt = now;
                        try { cop.Task.ClearAll(); } catch { }       // stopped: no more driving
                    }
                    if (stoppedAt != 0)
                    {
                        try { squadCar.Speed = 0f; } catch { }
                        // a moment standing still, then everyone gets out
                        if (now - stoppedAt >= 800)
                        {
                            try { cop.Task.LeaveVehicle(squadCar, true); } catch { }
                            if (Alive(searchPartner)) { try { searchPartner.Task.LeaveVehicle(squadCar, true); } catch { } }
                            try { me.Task.LeaveVehicle(squadCar, true); } catch { }
                            NextPhase(40, now);
                        }
                    }
                }
                break;
            case 40:    // at the station: the game's own get-out plays all the way through (nothing cuts it short:
                        // no clearing, no re-applied pose), then the game's own arrest
                {
                    if (me.isInVehicle())
                    {
                        if (t > 9000) { try { me.Task.LeaveVehicle(me.CurrentVehicle, true); } catch { } NextPhase(40, now); }
                        break;
                    }
                    // out of the seat happens as the door opens: the rest of the get-out (stepping out, shutting
                    // the door) still has to play. Done once he has stopped moving, or 2.5 s after leaving the seat.
                    if (stationOutAt == 0) { stationOutAt = now; break; }
                    float v = 0f;
                    try { v = me.Velocity.Length(); } catch { }
                    if (!((now - stationOutAt >= 1200 && v < 0.3f) || now - stationOutAt >= 2500)) break;
                    stationOutAt = 0;
                    stash[0] = 0; stash[1] = 0; stash[2] = 0;
                    SaveStash();
                    cuffPinned = false;
                    SearchToVanilla(me, true);
                }
                break;

            // ---- bike: off it, a metre away, arms up, searched pose, frisked from behind
            case 60:
                if (me.isInVehicle())
                {
                    if (t > 5000) { try { me.Task.LeaveVehicleImmediately(veh); } catch { } }
                    break;
                }
                {
                    Vector3 bp = veh.Position, mp = me.Position;
                    float dx = mp.X - bp.X, dy = mp.Y - bp.Y;
                    float len = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (len < 0.05f) { dx = -1f; dy = 0f; len = 1f; }
                    searchSpot = new Vector3(mp.X + dx / len, mp.Y + dy / len, mp.Z);
                    WalkStraight(me, searchSpot);
                }
                NextPhase(61, now);
                break;
            case 61:
                if (me.Position.DistanceTo(searchSpot) < 0.45f || t > 4000)
                {
                    try { me.Task.ClearAll(); } catch { }
                    me.Heading = HeadingTo(veh.Position, me.Position);   // facing away from the bike
                    PlayV(me, animArmsUp, true);
                    NextPhase(62, now);
                }
                break;
            case 62:
                if (t >= 1500) { PlayV(me, animArmsToSearched, false); NextPhase(63, now); }
                break;
            case 63:
                if (HandsOnHead(me, t))
                {
                    WalkStraight(cop, me.GetOffsetPosition(new Vector3(0f, -0.75f, 0f)));
                    NextPhase(64, now);
                }
                break;
            case 64:
                if (cop.Position.DistanceTo(me.GetOffsetPosition(new Vector3(0f, -0.75f, 0f))) < 0.45f || t > 6000)
                {
                    try { cop.Task.ClearAll(); } catch { }
                    PlaceAt(cop, me.GetOffsetPosition(new Vector3(0f, -0.75f, 0f)), me.Heading);
                    PlayV(cop, animCopFrisk, true);
                    NextPhase(65, now);
                }
                break;
            case 65:    // 5 s into the frisk, the roll
                if (t >= searchMs)
                {
                    PinCop(cop, false);
                    try { cop.Task.ClearAll(); } catch { }
                    if (Carried() > 0 && rnd.Next(100) < searchFindChance)
                    {
                        searchFound = true;
                        SayArrest(cop);
                        choiceKind = 2; copChoice = true;       // you stay in the searched pose
                        NextPhase(8, now);
                    }
                    else
                    {
                        CopSay(cop, "FOUND_NOTHING", 2);
                        try { me.Task.ClearAll(); } catch { }
                        searchSpot = veh.GetOffsetPosition(new Vector3(-0.7f, 0f, 0f));
                        WalkStraight(me, searchSpot);
                        NextPhase(66, now);
                    }
                }
                break;
            case 66:
                if (me.Position.DistanceTo(searchSpot) < 0.7f || t > 6000)
                {
                    try { me.Task.EnterVehicle(veh, VehicleSeat.Driver); } catch { }
                    NextPhase(21, now);
                }
                break;
        }
    }

    // armsup_2_searched_pose: held at HandsOnHeadPhase so the hands stay on the head (never replayed).
    // If the game won't report the animation time, it is held after a second; if it already finished,
    // searched_pose takes over.
    bool HandsOnHead(Ped me, int t)
    {
        float ph = AnimPhase(me, animArmsToSearched);
        bool playing = AnimPlaying(me, animArmsToSearched);
        if (playing && ((ph > 0.01f && ph <= 1.01f && ph >= handsOnHeadPhase) || (ph <= 0.01f && t >= 1000)))
        {
            AnimSpeedOf(me, animArmsToSearched, 0f);
            return true;
        }
        if (t > 600 && !playing)
        {
            PlayV(me, animSearchedPose, true);
            return true;
        }
        return t > 4000;
    }

    // the ride is driven exactly the way StopRightThere drives its squad car: the drive order is given again
    // every 600 ms (cop.Task.DriveTo, traffic laws off), or the game's wander when there is no police HQ spot
    // a car (or anything big) within a few metres straight in front of the squad car
    bool BlockedAhead()
    {
        try
        {
            if (squadCar == null || !Exists(squadCar)) return false;
            int me = squadCar.GetHashCode();
            Vector3 p = squadCar.Position;
            Vector3 f = squadCar.GetOffsetPosition(new Vector3(0f, 1f, 0f)) - p;
            Vector3 r = squadCar.GetOffsetPosition(new Vector3(1f, 0f, 0f)) - p;
            float half = VehDims(squadCar).Y * 0.5f;
            foreach (Vehicle v in World.GetVehicles(p, 14f))
            {
                if (v == null || !Exists(v) || v.GetHashCode() == me) continue;
                Vector3 d = v.Position - p;
                float along = d.X * f.X + d.Y * f.Y, side = d.X * r.X + d.Y * r.Y;
                if (along > half && along < half + 7f && Math.Abs(side) < 2.2f) return true;
            }
        }
        catch { }
        return false;
    }

    // ---- traffic around the search. The stop is a stretch of road: from 7 m behind the squad car's rear
    // bumper to 5 m in front of your front bumper, the whole width of the road. Traffic coming from behind
    // stops at the 7 m line, traffic coming from the front stops at the 5 m line (slowing in over the last
    // 10 m, so it does not slam). Nobody drives past or into the two cars until you are driven away.
    // Police cars and cars driven by the officers are never touched.
    float haltBehindCop = 7f, haltAheadOfYou = 5f, haltHalfWidth = 12f;
    int haltScanAt, haltErrors;
    class Held2 { public Vehicle V; public float Cap; }
    readonly List<Held2> halted = new List<Held2>();
    readonly Dictionary<int, KeyValuePair<Vehicle, Ped>> parkedCars = new Dictionary<int, KeyValuePair<Vehicle, Ped>>();

    // the stop is over: the cars he made wait drive on again
    void ReleaseTraffic()
    {
        foreach (KeyValuePair<Vehicle, Ped> kv in parkedCars.Values)
        {
            try
            {
                if (kv.Key != null && Exists(kv.Key) && kv.Value != null && Exists(kv.Value) && kv.Value.isInVehicle(kv.Key))
                    kv.Value.Task.CruiseWithVehicle(kv.Key, 10f, true);
            }
            catch { }
        }
        parkedCars.Clear();
        halted.Clear();
    }

    void HaltTraffic(Ped me, Ped cop, int now)
    {
        if (squadCar == null || !Exists(squadCar) || searchVeh == null || !Exists(searchVeh)) return;
        try
        {
            if (now - haltScanAt > 100 || now < haltScanAt)
            {
                haltScanAt = now;
                halted.Clear();
                if (policeHashes == null)
                {
                    policeHashes = new List<int>();
                    foreach (string n in new string[] { "POLICE", "POLICE2", "POLICE3", "POLICE4", "POLPATRIOT", "NOOSE" }) { try { policeHashes.Add(ModelHash(n)); } catch { } }
                }
                // the road axis: from the squad car towards your car (your car's heading if they are on top of each other)
                Vector3 cp = squadCar.Position, pp = searchVeh.Position;
                float ux = pp.X - cp.X, uy = pp.Y - cp.Y, ul = (float)Math.Sqrt(ux * ux + uy * uy);
                if (ul < 1f) { Vector3 f = Fwd(searchVeh.Heading); ux = f.X; uy = f.Y; ul = 1f; ux /= 1f; }
                else { ux /= ul; uy /= ul; }
                float rearLine = -(VehDims(squadCar).Y * 0.5f) - haltBehindCop;               // behind the squad car
                float frontLine = ul + VehDims(searchVeh).Y * 0.5f + haltAheadOfYou;          // in front of your car
                int sq = squadCar.GetHashCode(), mine = searchVeh.GetHashCode();
                foreach (Vehicle v in World.GetVehicles(cp + new Vector3(ux * ul * 0.5f, uy * ul * 0.5f, 0f), 60f))
                {
                    if (v == null || !Exists(v)) continue;
                    int h = v.GetHashCode();
                    if (h == sq || h == mine) continue;
                    try { if (policeHashes.Contains(v.Model.Hash)) continue; } catch { }
                    Ped d = null;
                    try { d = v.GetPedOnSeat(VehicleSeat.Driver); } catch { }
                    if (d == null || !Exists(d)) continue;
                    int dh = d.GetHashCode();
                    if (dh == me.GetHashCode() || (Alive(cop) && dh == cop.GetHashCode()) || (Alive(searchPartner) && dh == searchPartner.GetHashCode())) continue;
                    Vector3 vp = v.Position;
                    float rx = vp.X - cp.X, ry = vp.Y - cp.Y;
                    float along = rx * ux + ry * uy, side = rx * -uy + ry * ux;
                    if (Math.Abs(side) > haltHalfWidth) continue;               // not on this road
                    float halfLen = VehDims(v).Y * 0.5f;
                    Vector3 vf = Fwd(v.Heading);
                    float dir = vf.X * ux + vf.Y * uy;                           // +1 same way as the cars, -1 towards them
                    try { Vector3 vel = v.Velocity; if (vel.X * vf.X + vel.Y * vf.Y < -0.5f) dir = -dir; } catch { }  // reversing
                    float room;                                                  // metres its front bumper may still go
                    if (dir > 0.3f) room = rearLine - (along + halfLen);        // from behind: up to the 7 m line
                    else if (dir < -0.3f) room = (along - halfLen) - frontLine; // from the front: up to the 5 m line
                    else room = (along > rearLine - halfLen && along < frontLine + halfLen) ? 0f : 99f;   // across the road
                    // already past its line and inside the stretch: it stays put; well past the stop (driving
                    // away on the far side): left alone
                    if (dir > 0.3f && along - halfLen > frontLine) continue;
                    if (dir < -0.3f && along + halfLen < rearLine) continue;
                    if (room > 45f) continue;
                    Held2 hv = new Held2();
                    hv.V = v;
                    // a steady, even braking (2.5 m/s each second) that brings it to rest exactly at the line
                    hv.Cap = room <= 0.05f ? 0f : (float)Math.Sqrt(2.0 * 2.5 * room);
                    halted.Add(hv);
                }
            }
            foreach (Held2 hv in halted)
            {
                try
                {
                    if (hv.V == null || !Exists(hv.V)) continue;
                    if (hv.V.Speed > hv.Cap) hv.V.Speed = hv.Cap;
                    // stopped at the line: the driver takes his foot off the gas (no burnout, no creeping) and
                    // just waits there until the stop is over
                    if (hv.Cap <= 0f && hv.V.Speed < 0.6f)
                    {
                        int key = hv.V.GetHashCode();
                        if (!parkedCars.ContainsKey(key))
                        {
                            Ped d = hv.V.GetPedOnSeat(VehicleSeat.Driver);
                            if (d != null && Exists(d))
                            {
                                try { d.Task.ClearAll(); } catch { }
                                parkedCars[key] = new KeyValuePair<Vehicle, Ped>(hv.V, d);
                            }
                        }
                        hv.V.Speed = 0f;
                    }
                }
                catch { }
            }
            // a car he made wait keeps standing still while it is held (even between scans)
            foreach (KeyValuePair<Vehicle, Ped> kv in parkedCars.Values)
            {
                try { if (kv.Key != null && Exists(kv.Key)) kv.Key.Speed = 0f; } catch { }
            }
        }
        catch (Exception ex) { if (haltErrors++ < 3) Log("Holding traffic: " + ex.Message); }
    }

    void DriveToHq(Ped cop, int now)
    {
        lastDriveOrder = now;
        try
        {
            if (hqKnown) cop.Task.DriveTo(hqTarget, rideSpeed, false, true);
            else GTA.Native.Function.Call("TASK_CAR_DRIVE_WANDER", cop, squadCar, rideSpeed, 1);
        }
        catch (Exception ex) { Log("DriveToHq: " + ex.Message); }
    }

    // ------------------------------------------------------------------ arrest mode
    // One star: the cops try to detain. Run for it and the wanted level jumps so they open fire.
    void StartArrest(Ped me, int now)
    {
        try { if (Player.WantedLevel < 1) Player.WantedLevel = 1; } catch { }
        arrestActive = true;
        arrestPos = me.Position;
        arrestSince = now;
    }

    void UpdateArrest(Ped me, int now)
    {
        if (!arrestActive) return;
        if (now < arrestSince || now - arrestSince > 180000 || (now - arrestSince > 3000 && Player.WantedLevel == 0))
        {
            arrestActive = false;
            return;
        }
        if (now - arrestSince < 2500) return;
        if (me.Position.DistanceTo(arrestPos) > arrestFleeDistance)
        {
            if (Player.WantedLevel < fleeWantedLevel) Player.WantedLevel = fleeWantedLevel;
            arrestActive = false;
        }
    }

    // ------------------------------------------------------------------ audio: .wav files played from memory
    void LaterChance(int ms, string folder, int percent)
    {
        if (rnd.Next(100) < percent) Later(ms, folder);
    }

    void Later(int ms, string folder)
    {
        Delayed dl = new Delayed();
        dl.Born = Game.GameTime;
        dl.At = dl.Born + ms; dl.Folder = folder;
        delayed.Add(dl);
    }

    void UpdateAudio(int now)
    {
        // player lines that are due: the earliest one plays once nobody is talking (dropped after 10 s of waiting)
        for (int i = delayed.Count - 1; i >= 0; i--)
        {
            Delayed dl = delayed[i];
            if (now - dl.Born > dl.At - dl.Born + 10000 || now < dl.Born - 60000) delayed.RemoveAt(i);
        }
        int due = -1;
        for (int i = 0; i < delayed.Count; i++)
        {
            Delayed dl = delayed[i];
            if (now >= dl.At && (due < 0 || dl.At < delayed[due].At)) due = i;
        }
        if (due >= 0 && !PlayerTalking() && !NpcTalking(now))
        {
            string folder = delayed[due].Folder;
            delayed.RemoveAt(due);
            PlayPlayerNow(folder);
        }


        // NPC lines from their own voice, one at a time, never over the player or another NPC:
        // try each context until one actually plays
        if (speeches.Count > 0)
        {
            Speech sp = speeches[0];
            if (!Alive(sp.Ped) || now - sp.Born > 12000 || now < sp.Born - 60000) speeches.RemoveAt(0);
            else if (now >= sp.Next)
            {
                bool talking = false;
                try { talking = GTA.Native.Function.Call<bool>("IS_AMBIENT_SPEECH_PLAYING", sp.Ped); } catch { }
                if (sp.Started && talking)
                {
                    lastSpeaker = sp.Ped;            // it plays: done with this one
                    speeches.RemoveAt(0);
                }
                else if (!sp.Started && (PlayerTalking() || NpcTalking(now)))
                {
                    // wait for the current speaker to finish
                }
                else if (sp.Index >= sp.Lines.Length)
                {
                    if (sp.Cop && !sp.Retried && copVoices.Length > 0)
                    {
                        sp.Retried = true;
                        sp.Index = 0;
                        try { sp.Ped.Voice = copVoices[rnd.Next(copVoices.Length)]; } catch { }
                        sp.Next = now + 150;
                    }
                    else speeches.RemoveAt(0);
                }
                else
                {
                    try { GTA.Native.Function.Call("SAY_AMBIENT_SPEECH", sp.Ped, sp.Lines[sp.Index], 1, 1, 0); } catch { }
                    sp.Started = true;
                    sp.Index++;
                    sp.Next = now + 250;
                    talkGraceUntil = now + 500;
                }
            }
        }
    }

    Ped lastSpeaker;
    int talkGraceUntil;

    // a player .wav is still playing
    bool PlayerTalking()
    {
        return playerVoiceOn && playerVoiceUntil - Environment.TickCount > 0;
    }

    // an NPC line is playing or about to start
    bool NpcTalking(int now)
    {
        if (now < talkGraceUntil && talkGraceUntil - now < 5000) return true;
        if (speeches.Count > 0 && speeches[0].Started) return true;
        if (lastSpeaker != null)
        {
            bool t = false;
            try { t = Alive(lastSpeaker) && GTA.Native.Function.Call<bool>("IS_AMBIENT_SPEECH_PLAYING", lastSpeaker); } catch { }
            if (t) return true;
            lastSpeaker = null;
        }
        return false;
    }

    // a cop line: the context first, then its numbered variations in random order
    void CopSay(Ped cop, string context, int variations)
    {
        if (cop == null) return;
        List<string> lines = new List<string>();
        lines.Add(context);
        List<string> numbered = new List<string>();
        for (int i = 1; i <= variations; i++) numbered.Add(context + "_0" + i);
        while (numbered.Count > 0) { int k = rnd.Next(numbered.Count); lines.Add(numbered[k]); numbered.RemoveAt(k); }
        Speech sp = new Speech();
        sp.Ped = cop; sp.Lines = lines.ToArray(); sp.Index = 0; sp.Next = Game.GameTime + 100; sp.Cop = true; sp.Born = Game.GameTime;
        for (int i = speeches.Count - 1; i >= 0; i--)
            if (speeches[i].Ped == cop) speeches.RemoveAt(i);
        speeches.Add(sp);
    }

    string PickWav(string dir)
    {
        try
        {
            if (!Directory.Exists(dir)) return null;
            string[] files = Directory.GetFiles(dir, "*.wav");
            if (files.Length == 0) return null;
            string last;
            lastPick.TryGetValue(dir, out last);
            string pick = files[rnd.Next(files.Length)];
            if (files.Length > 1)
            {
                int guard = 0;
                while (pick == last && guard++ < 20) pick = files[rnd.Next(files.Length)];
            }
            lastPick[dir] = pick;
            return pick;
        }
        catch { return null; }
    }

    // Sell during the hand-over, then (SellAfterChance %) SellAfter 2 s after that line has finished
    void PlaySellLine()
    {
        PlayPlayer("Sell");
        int left = PlayerTalking() ? playerVoiceUntil - Environment.TickCount : 0;
        LaterChance(Math.Max(0, left) + 2000, "SellAfter", sellAfterChance);
    }

    // the protagonist's line: scripts\Pusher\<IV|TLAD|TBoGT>\<folder>\*.wav
    // If someone is talking it waits its turn.
    void PlayPlayer(string folder)
    {
        if (PlayerTalking() || NpcTalking(Game.GameTime) || delayed.Count > 0) { Later(0, folder); return; }
        PlayPlayerNow(folder);
    }

    void PlayPlayerNow(string folder)
    {
        string path = Path.Combine(Path.Combine(baseDir, episodeName), folder);
        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))     // one set file, not a random pick
        {
            if (File.Exists(path)) PlayWav(path);
            else Log("Missing voice file: " + path);
            return;
        }
        string file = PickWav(path);
        if (file != null) PlayWav(file);
    }

    // an NPC line in his own game voice: the contexts in random order, the first one his voice has plays
    void Say(Ped ped, string[] lines)
    {
        if (ped == null || lines == null || lines.Length == 0) return;
        string[] order = (string[])lines.Clone();
        for (int i = order.Length - 1; i > 0; i--) { int k = rnd.Next(i + 1); string tmp = order[i]; order[i] = order[k]; order[k] = tmp; }
        Speech sp = new Speech();
        sp.Ped = ped; sp.Lines = order; sp.Index = 0; sp.Next = Game.GameTime + 150; sp.Born = Game.GameTime;
        speeches.Add(sp);
    }

    // Exactly like GreetTauntRob: the .wav is converted to 16-bit PCM with every sample scaled by
    // VoiceVolume / 1000, kept in memory, and played with the Windows sound player.
    readonly Dictionary<string, byte[]> voiceData = new Dictionary<string, byte[]>();
    readonly Dictionary<string, int> voiceLenMs = new Dictionary<string, int>();
    int playerVoiceUntil;
    bool playerVoiceOn;          // a line has been started (the Windows timer can be negative, so 0 can't mean 'none')

    void PlayWav(string path)
    {
        try
        {
            byte[] wav;
            if (!voiceData.TryGetValue(path, out wav))
            {
                wav = ToPcm16Wav(File.ReadAllBytes(path), voiceVolume / 1000.0);
                if (wav == null) { Log("NOT A STANDARD WAV: " + path + "  (" + lastWavInfo + ")"); return; }
                int ch = BitConverter.ToInt16(wav, 22);
                int rate = BitConverter.ToInt32(wav, 24);
                voiceData[path] = wav;
                voiceLenMs[path] = (int)((long)(wav.Length - 44) * 1000L / (long)(rate * ch * 2));
            }
            if (snd != null) { snd.Stop(); snd.Dispose(); snd = null; }
            if (sndStream != null) { sndStream.Dispose(); sndStream = null; }
            sndStream = new System.IO.MemoryStream(wav);
            snd = new SoundPlayer(sndStream);
            snd.Play();
            int len = voiceLenMs[path];
            if (len < 300) len = 300;
            playerVoiceUntil = Environment.TickCount + len + 150;
            playerVoiceOn = true;
        }
        catch (Exception ex) { Log("Audio " + path + ": " + ex.Message); }
    }

    string lastWavInfo = "";

    // GreetTauntRob's converter: any PCM / float .wav to 16-bit PCM, scaled by gain
    byte[] ToPcm16Wav(byte[] src, double gain)
    {
        lastWavInfo = "";
        if (src.Length < 44) { lastWavInfo = "file too small"; return null; }
        if (src[0] != 'R' || src[1] != 'I' || src[2] != 'F' || src[3] != 'F') { lastWavInfo = "not a RIFF wav"; return null; }
        if (src[8] != 'W' || src[9] != 'A' || src[10] != 'V' || src[11] != 'E') { lastWavInfo = "not a WAVE file"; return null; }

        int pos = 12;
        int tag = 0, bits = 0, channels = 0, rate = 0, dataStart = -1, dataLen = 0;
        while (pos + 8 <= src.Length)
        {
            string id = System.Text.Encoding.ASCII.GetString(src, pos, 4);
            int size = BitConverter.ToInt32(src, pos + 4);
            int body = pos + 8;
            if (id == "fmt " && body + 16 <= src.Length)
            {
                tag = BitConverter.ToUInt16(src, body);
                channels = BitConverter.ToUInt16(src, body + 2);
                rate = BitConverter.ToInt32(src, body + 4);
                bits = BitConverter.ToUInt16(src, body + 14);
                if (tag == 0xFFFE && size >= 26 && body + 26 <= src.Length) tag = BitConverter.ToUInt16(src, body + 24);
            }
            else if (id == "data")
            {
                dataStart = body;
                dataLen = size;
                if (dataLen < 0 || dataStart + dataLen > src.Length) dataLen = src.Length - dataStart;
                break;
            }
            if (size < 0) { lastWavInfo = "bad chunk size"; return null; }
            pos = body + size + (size & 1);
        }
        lastWavInfo = "format tag " + tag + ", " + bits + "-bit, " + channels + " ch, " + rate + " Hz";
        if (dataStart < 0 || channels < 1 || rate < 1000) return null;

        int bytesPer = bits / 8;
        bool isInt = (tag == 1 && (bits == 8 || bits == 16 || bits == 24 || bits == 32));
        bool isFloat = (tag == 3 && (bits == 32 || bits == 64));
        if (!isInt && !isFloat) return null;

        int count = dataLen / bytesPer;
        byte[] outData = new byte[count * 2];
        for (int n = 0; n < count; n++)
        {
            int p = dataStart + n * bytesPer;
            double v;
            if (isFloat) v = (bits == 32) ? BitConverter.ToSingle(src, p) : BitConverter.ToDouble(src, p);
            else if (bits == 8) v = (src[p] - 128) / 128.0;
            else if (bits == 16) v = BitConverter.ToInt16(src, p) / 32768.0;
            else if (bits == 24)
            {
                int t = src[p] | (src[p + 1] << 8) | (src[p + 2] << 16);
                if ((t & 0x800000) != 0) t -= 0x1000000;
                v = t / 8388608.0;
            }
            else v = BitConverter.ToInt32(src, p) / 2147483648.0;

            int s16 = (int)Math.Round(v * gain * 32767.0);
            if (s16 > 32767) s16 = 32767;
            if (s16 < -32768) s16 = -32768;
            outData[n * 2] = (byte)(s16 & 0xFF);
            outData[n * 2 + 1] = (byte)((s16 >> 8) & 0xFF);
        }

        byte[] wav = new byte[44 + outData.Length];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BitConverter.GetBytes(36 + outData.Length).CopyTo(wav, 4);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
        BitConverter.GetBytes(16).CopyTo(wav, 16);
        BitConverter.GetBytes((short)1).CopyTo(wav, 20);
        BitConverter.GetBytes((short)channels).CopyTo(wav, 22);
        BitConverter.GetBytes(rate).CopyTo(wav, 24);
        BitConverter.GetBytes(rate * channels * 2).CopyTo(wav, 28);
        BitConverter.GetBytes((short)(channels * 2)).CopyTo(wav, 32);
        BitConverter.GetBytes((short)16).CopyTo(wav, 34);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BitConverter.GetBytes(outData.Length).CopyTo(wav, 40);
        outData.CopyTo(wav, 44);
        return wav;
    }

    // ------------------------------------------------------------------ UI: drawn exactly like GreetTauntRob
    // Same font file (scripts\GreetTauntRob), same text rendering, same button icons, same layout.
    const float UiEdge = 0.975f, UiTop = 0.836f, UiStep = 0.029f, UiIconScale = 0.62f;
    const float UiOptionH = 0.026f, UiNameH = 0.030f, UiInfoH = 0.020f;
    const float UiOptionScale = 0.24f, UiNameScale = 0.27f, UiInfoScale = 0.20f;
    enum Ico { LB, RB, LT, DpadLeft, DpadRight, RT, DpadDown, None }

    class SpriteCmd { public Texture Tex; public float Aspect, Cx, Cy, H; }
    List<SpriteCmd> uiCmds = new List<SpriteCmd>();
    List<SpriteCmd> uiPending = new List<SpriteCmd>();
    bool spritesBroken;
    int resW = 1920, resH = 1080, lastRes;
    float uiAspect = 16f / 9f;
    Texture tLb, tLbOff, tRb, tRbOff, tLt, tDl, tDlOff, tDr, tDrOff, tRt, tRtOff, tDd, tDdOff;

    class TextTex { public Texture Tex; public float Aspect, CapCenter, PadRight; }
    readonly Dictionary<string, TextTex> textCache = new Dictionary<string, TextTex>();
    System.Drawing.Text.PrivateFontCollection uiFonts;
    System.Drawing.FontFamily uiFamily;
    bool uiFontOk;

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    const string IconLb = "iVBORw0KGgoAAAANSUhEUgAAAFsAAAA0CAYAAADllEQWAAAJr0lEQVR42u2cXWgc1xmGn29mtLvaXa3+bcl2oHZV0srUEW6JQ+0UQ90QCHIhFzY1beokJtQuhTYESnPTQghuLpJC6E1JchEIpWoSmlKKcWJKwW0dHGIXp3aqJI0vQn4kunbsSNrV/H292DOb2dVKWsuaVeTog2HR7NE5M++85/1+zpkVVthUVQCJnwIQEZ2nvQ10m6MAdAHtQBuwHvCBLPBlIA2Edf1HJoAHlGLj2sBl4E3ABT4BTonIdHSdIhIu9V5lhcC1zdihiATztEsBQ8CwAe4mIA/0Af1ArwG8I+FL/jtwDDgqIqqq9nzXvOJgG3CjsaT+QlW1G9hkGPo14FvAVww7c4a9C1lojmgMjcZq9hIXOGeZA+A0cK+IXFBVaykMl4TZqw3AzRpAvwoMADuB7Yt0GdTJgdQdSVpgwHeAS8ABETm+FIZLEiCLiB87lwfWAd8B7jYSMGQeRpydWgfmisrdPKDbRse/KyJ/UVUnfq+Jg62qVr08GCf2beAW4B6ju/XmmU87NlU/6xaaa/0YuEdE/nwtDJdlZvEQ8GPg68A3Guhg2MLpnzTg08D3ROSlZhkuS2QykYNQ1XXALuCnBuRMDFzfaN1qBXYxwGcMw19sBnC5RiZb0ZRR1UHD4r3A1jptizz5jQZyI8A/AX4gIn9cDHBpls0xJm8CfggcMkkEhsH2Z8iZtRrwWeD7IvK8qjpA0CgpkyYkQ00w3w38BPiRSSjiLLb5/FoE+BRw0EhKQ6cpCwBti0hgIot7gJ8BN8cGkM8ZixdLjAQoG0n5QyNJkfkyPsPmYeBXwGidXKyBvLDTvNcAXsPwhZh9L3DU6HIYc3rXbb7vo1oraY7jICLX3U8ztpSxlgD4QaPhVcClQQ1jHfBL4wTjmdONNe9VkwI8khTXSMrvI8DjYDsi4qvqiyatXlY2RzcXhiFjY2OMj4+TSqXwfZ98Ps99991HV1dXUyCEYYhlWTz33HOMj4/T3t5OGC5cF4rGzmaz3H///XR1dREEAbZtJ8nwaWCPiLwa5SeoqpijTVXPq2qgqr4uo4VhqKqqvu/r3r17FdBcLqeWZenGjRv14sWLNe0WMs/zVFX1jjvuqPaTyWSaOrLZrO7evVvHx8ebHm+JFpjPt1S1E5PdmQcvoapuoFIztmLMXnbr7u5mYGCAfD5PuVxm3bp1OI5zzf309vbS29tLd3c3QVDxQ5ZlNZwZYRhWZ82ZM2c4cOAAx44do6+vLylJsUxA8SXg16p6yKpzlEOxRCWxiCMIAjzPqx5LdXS+79f043kexWKRycnJmuOjjz6iXC4TBAGu69Lb28vbb7/NM888U30QCZltSLsfGHHqgN1IZSlJV6PTC4KA0dFROjs7qw9PRCiXy5w8eZJisUg2myUIAtLpNMePH+eBBx6gp6cnKXaLATsL7K4He0Os/JlaTUCLCEEQ8NhjjzE4ODjn+5MnT3L48GGKxSKpVIpUKsXExARTU1NJgh3JiQL7rFi4Qqxit2qTlmKxiOd5zM7OVqWlVCpx++23Mzw8TKlUwrIqt+26blXrk+SBOXY4dTWOm1Y72KlUira2tppz0d+e51Vngeu6bN26lb6+vuq5pEN7R1XFpOY5KgutqxrsmZmZqjO0LIswDJmZmeGFF17g9OnTdHR0ICJcvXqV0dFROjo68H1/SdHQtTI8KuwrlS0CNy9nItNqs22bhx56iHw+X5UH27Y5f/48ly9fJp1OY9s2ExMT7N+/n0OHDqGqSSU29TYbf5wFoCeW/ay69Nu2bd544w3CMKyRhVQqRS6XqzL/8OHDHD16tApywhISpe9vxkGNluyVVWzpdJr29nYymQzpdJpUKoWqVkNBy7I4e/YsJ06cqIK8lBj/GsEG+E8c7G2s8hp1FL7Zto1lWWSzWXp7e+nq6sJ1XVzXJZVKce7cOfbt28cTTzzRCsCjjOm1uIyMrGZGiwie5zE2NsbQ0BBhGFZLqarK+++/z4MPPsj58+fJ5XJkMhkeeeQRtm3bxp49e+ZITwL2lhWj+faVZuVy9DEwMMD69esZHBykv7+fvr4++vv7GRkZ4dFHH8VxHHzfx7Is2traePzxx6vykpBFdaaSE1X9TF2k5WFfVPqMEozFMrnF2Dc7O0sYhjXlU1UlDEOGh4dJpVJ4nlct005MTOB53pzYfJlLre8C7zjmBtSsNa6YBBQKhaZDsEaFI1UlnU6Ty+WwLGsOU23b5tSpU5TL5SqwIoLv+0xMTLBp06YkUvYI7HPAe44B+oskv/W2IUCWZVEqlXj66acZHBwkCIKGNxy12759Ozt27Gg8Xy2LCxcuUCqVcF232o+I8Morr/Dss8/WPAQRwbIsNmzYkFQIGGnjhyISRg5yi6lMtdwiEJ988skF2zmOQ7FY5MiRI9x2223zVv2OHDmCZVk1PiDKGEWETCaDqtLW1salS5c4ePBgtX0CYDsG8H9Ff0Bl+266FQlNGIZ4njdnqi+WLjuOQ3t7e027qI84uEEQ4PtzNyXl8/lqZBKGIZOTk+zatYuHH344qbAvSmauACfiYH8hltgkDvb09DSu62LbdtMhl+M4lMtlXNetcYYzMzNkMplFq3eu61argYVCgTvvvJOnnnqKfD6fFKsjsD8UkXdVVSKw062SDcdxGBoa4oMPPiCbzTa9SmLbNj09PTW16s2bNzMyMkKhUFgQ7KjWPTAwwJYtW9i5cyd33XVX9eEnGPYp8FL1Osyq70tUNuL4MbYnZlevXmVqamqOti4WsQRBQKFQoKOj4suvXLlS4wgXc8aFQoF0Ol0T1yeYyERbQG4VkddU1XKo7BPZ3MpqX6FQoFAoXHc/nZ2d1/w/vu9XU/ok1dIA/S5wMe4tB6i8QNSyhCZeGFpKTB6vZzSrt1G7FtStMQqRAp4Xkf9Fm3QisAutkpB6wFrVTwtWYuI67VBZxz0dJ7FlnOOqXCz4jFoUPr8DvGx8YhCBvSI1kRvYopWvv4nIVGVSVTbGO3y6FLYG9vJV+QB+awp8YfwLew2fZZUQBV4F/l3/qocFvMenr82t2fWDLcBvzFsbThxwC/gnny6H6Rpe1x1bXwT+GjtXw+zXgbOmYbCG2XWzekxEPjSsDus9J6r6TeBlEwZ63JgviiYZV/tUfu/kPSpruZdNbF+jFBK946iqo8DvqOzPjrKgNWlZPMSLEsFJ4G4R+Ue0y6zRP6CqbSLiqeotwM+p/IJCZg3PpmwK+BPwCxH570I/HBB/pyb+Fu+tVGrcXXVtAyrF8OWyvKkh6DIxbZrKu4jLIYFtsVk+33gfA++IyOv1GDay/wPRc1GMcufvqQAAAABJRU5ErkJggg==";
    const string IconLbOff = "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";
    const string IconRb = "iVBORw0KGgoAAAANSUhEUgAAAFsAAAA0CAYAAADllEQWAAALE0lEQVR4nO2cbWwUxxnHf8/u3vkcg22QA1FKHNSkiagMIYVCUa0IRSooLgVqhEFISWwhJUrykUStP0SUSKiBL0GiVVKJKJCitgjFAiIhuQGnaigU05TaUKAolASSQHkRjvHLcbe70w9zs7d3vrMNvru8+S+tVje398zsf5993mbmhDFAKWUBSkSUUqoCWABMBKLADGAS4AGS+kk5EAFULnGABdwCzgADgAP8D0gCg0AP0AvcAG6IiJdnXKY/CTeLSK5+SwYZ+ZLcUErZIuKlbqwVeAKoL9jIcuMmmujrwFXgGtAHXEQ/oFPARyKSyDdm9ANVgFdq8u+IbKWUJSK+Uur7wFvAvNRXfurIJ3u0/anUIaTfAit1DIdeoB/9VpwGDgIfot+IT0XkRtZ92KG+KDb5t012SKMXA38AJgNuSpZd4PEN6T7rMG3WKPr+J/A34DJwAjgoIgMZwjX5QpG0/rbIVko5IuIqpX4K/BFtnz2KT/JokE1O+GEImW+FB3yENkVtwF7gioj0BT9WyqHApI+a7JBG/wx4G6hGm4yRXu2vCnw0yaCddDZOoe+rC3gv7HyNuRERP8fvRo1RkR3S6OXATqCCrxfR2QibIouhPBwG/gFsFZGPgh+NUdtHJDtE9Ar0k7+LrzfR+aDQvschzUscTfprwCERuQJByMvtavqwZIeI/jmwA22jv4lEh6FIR1RhX/RvYB9a2y9BYF780Wp6TrJTsbOdInol8HugjG8+0dkwJHpojQedZG0D3hCRTyEdCo8kLB/ZxhmuALYDE/j2EZ0N4zCNtl8HfgtsEZEbKQWV4UgfQnbIdDShTUeMdPg0jkzHCvAfYBPwdkpB7XxlhAwCQxrdhM4Mv6nOsBBQZJqXd4FfisgpU5vJtuUB2SGiV6JNR0GJVkqh1Mh+REQQGf4lKqSsAsCYDQttz1tF5K2c44EMolejTUeUL9F0eJ5+C2177Imp7/v4vo9t28UmPpxJvwH8CrgCaQ2XUFHpR8ABipSwfP755wwMDAx7TSwWo6amhlgsBhBobzZJo5EVjUaprq6msrIyaPN9H8sqqkUMa3mbiKwwPhDSml0FHAO+R4GJ9jwP27ZZvXo1HR0dWJYVaG4YIkI0GqWmpobZs2fT1NREQ0MDqfEhIrclKxKJUFVVxUMPPcTSpUtZs2YNsVgskFVEmPr9GWA2OlFCRJSVyoZeQxPtUiJnKCLYtp1xJBIJPvvsM/bv309zczMtLS309/cDDGujs2VZloXruly9epXDhw/T2tpKQ0MDFy9eRETw/TGVOEa8NTSHE4B7UyZESDXOBlahNbqo1TujUeZs7Knv+4GGhonbv38/a9euzekQh5Pl+35wvWVZOI7DyZMnaWlpIZlMAsM/vLHeZuo8FXgw3OYAC9GRh0eRtVopFdjMOXPmsHz5cjzPQ0To7++nq6uL9vZ2XNdFRHAch46ODvbs2UNjYyOJRCJwmmFZ8+bNY9WqVbiui23bxONxuru7aWtrI5lM4vs+juPQ3d1Ne3s7S5YsCUxSsW4VnXF/J/U5ILuJzCC9qDCa+Mgjj/Dss88O+b6jo4Pm5mbi8XhA6M6dO2lsbBxia83nuro6nnzyySGyli5dytNPPx2YDRHhyJEjLFmypNianUBHdPeG2rCA+akPJQ3z4vE4rusGZ9d1SSQSPP7446xYsUKPMOUUT58+zcDAAJFIrjJ0blmu67Jo0SJmzpyZQWxfX19OGQWG4TKWOitIT36WHMZMhA8TXcyaNSvj2v7+fnp7e4eVlX3k0lylFNOnTy/0reQcUup8X+rsQWbttuQwjs8Q43keZWVlQ7TPcRyi0WheOdFoNHCqBrZt8/7773PixAkikQiJRIJoNEpjYyNAseNtw+kMpVSFiPQrpcRBr9MoK2bPuWAyurBpKCvTw9i7dy+QdoJTp06lqqoqb1Jy4cIFPvjgAzzPw7IsBgcHOXr0KNu2bcPzvMAJb9myhfvvv78UyY0R/jBQg57xFwc95T+bEqfn8Xicnp6ejAji3LlzbN26le7uboDAHCxcuDCIw8MabkK8AwcOcPDgwSF9KKUoLy9n2bJlrFu3junTp+O6Lo7jDLm2wBB0KD0ZCFJYh3SmUxKyDUFtbW20t7cH7bdu3SIejwefbdvGdV1isRjPPffcHWV+lmXh+z7nz59n165dtLS0MGXKlFJkkZAuxQYproNO01dT4lJqMpmkp6cno8282kqpwH5v27aN2tranK++IWzatGnMmDED3/eDDPHy5cucOnWKRCJBZ2cnx44dY/fu3bzzzjt55RUYJsKbhZ65xwHOFrPHvCPJ0iylVECAiFBfX8+GDRuoq6sL2rPrICbyWLRoEZs2bRrSx6FDh3j++ee5cuUKjuNw4cIFXnjhBfbt21fUe8vCbOBPoMkepIRabUieNWsWixcvDhKON998k56ensB83H333dTV1ZFMJvPG1waJRALP8wJ7bGx9fX09mzdv5qmnnsJ1XSzLorOzk66uLh599NFiZ5EGP0idlYNeGfQx8F1KQLrRxrlz5/LSSy8F7RUVFbzyyiuBPW1ra2PNmjU89thjoyLFtm2UUhnpvOu6zJ8/n4qKCvr6+ohEIiilArKLmEVC2v89aGZuLPQK0O7UF0Uth4Vhsr3BwUGSySRr165l2rRpGcRu3LhxVM7MEBs+kskkjuNw8+ZNEolEhgxTjCoRbDN5YKbgL5lxl2oEJoOMRCKICOXl5bz44otAush//Phx9u7dG5RM8yEWi+E4TnA2SVBvby/r168nmUwGjlMpRW1tbTCGEmCiUuoBEVEm4PwXmuiiB6C5YNs2vu+zcuVKXn/9dc6ePRs4yo0bN/LEE08EJiAbSikOHz7M+vXrg6TG932uXbvGkSNHuHRJ65Gx45MmTWLBggUZVcMi4y60iT5nyD2AXttcRZHi7ezaRfhGjdZFIhFaW1tpbm4GdCj4ySefsGPHDp555pmgxBquf4gIp0+f5syZM0P6VEoFD8mUbTds2EBlZWUpnKNJbMqAmcB7llJKROS/FNmUeJ4X3LQ5h2G0u6Ghgblz5wbRBcDmzZu5fv16EJVky4LMOku43uK6Lr7vM3HiRF599VVWr15dqigE0gnNdMg0G3uAXxSr18rKSqqrq3EcB9d1mTBhwpBrDEEvv/xyoN0iguu6bN++nXXr1mXIGm6Ky7IsKioqqK2tpb6+nqampiCZKRHRYZRB5uz6D4FOirS4vb+/PyMKKCsro7y8PO/1X3zxRcbsuud5TJ48OaesXDBON1xLKaFGG5hVse8Cy8Nk1wBHKVG8PRwKWbswc5KWZZXKIWZ0j+bxJPATQ7QtIteA3amL8sdZd4h8NjUXjPPLd32u7/IdZsL3SyAa0oHGNOAeJ6uxE73n0KHAUcntaupw15coPi4EBK24lcA95nF7qfUjf0an7xYlzCa/BbCAMrNdQemT9AF/YXyJcCER1EjChsxPFUx+x9CtbOO4cxiyH84gNKXhJ4G/k7m3ZBxjhx2QLXqzv5NaNf8b0unmOMYGH624F7NNhSG3AziPTm7GCb9zGN8nwOFsM+KntPsSsItx7R4rTDZ+HPgw1wYm0zYJXXq9j3TsPR6hjA5mA2sEvS5nkYj8Ne8+yJQN/zF6I/2U1Ffm3xe+1D9J+QrDcGOSxT5gjYi8q5Sy8mpqaJ/NA8AGYBl6gfc4RkYc/U8PvxaRLqVURESSI22nDnauKqXmoBd3VzO8Zvehzc5YodCrQCtG6G+0MEt5C7mMtQptk8Pj6wE+FpFOyOTw/796tYotDTNlAAAAAElFTkSuQmCC";
    const string IconRbOff = "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";
    const string IconLt = "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";
    const string IconDl = "iVBORw0KGgoAAAANSUhEUgAAAEcAAABICAYAAACk5ujKAAAGY0lEQVR42u2cTYgcRRTHfzXTk92NiavrbhIiSwiiiYKIJwXxICaevKhBEE8ecvDkUU9BPIQoiBcvuerRDxC8mIBBJEcNXgQ/COTDGE02H7trsslM999Dvdqp6Uw2menuSYb0g2J7unurq/796tV7/1ddjgpFknPOKXeuCbSAGWAjIGAC2AI07bZFYAFIgAz4F1gC2nF9/eovUxwVi6Qp4AngWeBp4AEDYRrYYOCsAx6KwFkGLtvvzIC6Yud/B34AfnLOXWFcRdKspA8l/SXpmsqRVNI5SZ9I2jyOoDgreyNQMkkdSdclte04jUosWe5a+L+OXZPVsU9Sq6p+NCq0MzPAmzZkUrsc7E1ixy4a2itR6UTXXPR/YdilVsceYC48t+y+JBUq0AZg3mxK6OSfZjNS4Cxwwc5fBU4YKJj92QK0DZB5s1GPWXFW7/32Es5U0YEqwUkio5/Z8efAfrvWDuf7zThBA00jWnbvO8BHUb0u0qaxAqeftJ1zHUlpnym+Z1iE6wZQO/wdZWNHDY4zEBqSshigW/grDUnZKFyPSg3yLdHxIGhA501VOnt3DTgFpWVtDkBlwLUaHC/rcr8zM+w1OPWwqsGpwanBqcEZTpp9nLZODY6XySjuCYHi9RqcLiA9Xm7kvI2FJDfjZArGTyPthLW30HP7hSdJ/gHOuaxgHCOrTyPQxjjuUgkgN+I4LskHhHbTRIFGOzO8k7nzaQmufjsHggMmjSptRteG0fy2cy4NiuKcU6w5k8AzwPPA9gHpjGYfIObwjF5o7C/4rAFDvOVw/2HgLWCH/Z4DDgDnc8a/MSBAAk5LOgIcdc4trw4nSROS3pV0NiKwVWK2QJK+shcwsE0L90uaknTI6utU0NaLkvZL2rDaRkm7LN0ROpOVWCTpN0kvh6xEEcMraY+k41GHOiWUNGrroqQ34gcejB5UhdYcjJ7lis5Mkl6X9KukSyW2NYv6/5kkl3BjlqAKubrWlDnolCvpa+AP4BHgFWATXRKf3PHt2LLt+KxGOLcFmEmArcBs5MUyoim4CEAd4BhwTNJhmxmVczZ1m+0S8DawzyYhZ+BsC+BsqqITI3D8cM5dxufVi9R13NyEkD2dAeYS/EqH9eMWMYfhWdSbN805E/lgAh4M4MzaDzGGUsSGRYnDxchOCZgC1jfo5q3HaliNIGBWI+dy6x4FJOvDNbVqJrA7lNJ8SFSDc4fIrhqcGpx7Y4bKr/NJY96jytjqbpdmH/6q3cAvYV2pp/KemcsBLrG4ZMm8wvGbg4snAxxwX2RinCnLSgL8F1MKYxh4FtH2wBlvpXcN4yVgIcEvnb8AbBvTwHM6R1kMKh1jJpJoSC0BlxPgHwOnSqOskrUmAZ7k5mTXoNozH4GDYXIqwX9XcKKKTkQyFZHkrmgkDbwKvG9vfLrEF5iZ7TkJnG445zLgG7ofYqjEErTwJeC1MigGq+cA8LgBk5ZQsmhKXwa+CzksLBXxgaQFlS+BZD9kX9CMQ2pmYyDYcc4tSzoA/AjsAh7uY3vSAYZdiifRXqD72dA8sBPP/Q4rO+kmA8JbPkJvUm9QzzhM4aetrtWkXpwOvgIclvQ9XS51GKPqDJxHgW8NnAyfpdxt4AzqcIb7d1s9mYFxDngPn4loDmkzgxKsmQ4OCwnSPtzGMPZhhRvz2kU//2nlNFrAinOuTQlLbm93IUEZpHWjgilda3i5hcIf55xsclqVZC0Ha0j0Q+JtZHGaPY+yPwGokrJIoiGwOq5rPsfLRG4GqdcE9pkFKgkhaiawBqcGpwanBqcGp5YanLsNnGgpvhvi/mwNymHswQkRb5bfBycufeK8cP9VehnGJp5cryz+GaW0jBxv2u4CulmgG4HUABJJHXpJcCKAxg6cwOfEi6P2Ai/ivwU/YZrgJJ3E77YUqIfNeCZR+ITbHH7dYrzJRyjtcQRnATiFz4eFrRZ20P1uIY632niCzUXtaq4RowW+aBG4ODY2J9q5ZAH4Ap80i/e9adu5LOpoC5+OnqT7hV+4ltr9AcAwlDrAl3iqlDuxnUPRmWmTpI8l/W07J5UhHUnnJX0atqWqYmOhfipbBUBTwFPAc/iNzaZNU2bpXRQ9FQ2bRXxmIdiuJbNTF/EbFB0Ffl799GdEnEvpPk1+yrahHLa+W2dgbDbDG4bSBQMogHPJjPdIt8L7H1oH7A55iELgAAAAAElFTkSuQmCC";
    const string IconDlOff = "iVBORw0KGgoAAAANSUhEUgAAAEcAAABICAYAAACk5ujKAAAFhklEQVR42u2c224bNxCGv1mtbdmpHfeAoocUPSBFm7ZpLwo0r9BX71V8UTQoUvfsJBdNUdd2bJ12esFhNVqvZEnLFSxnCRCWVjSX/DkczvxDUmgwqSoiUn4mQAZsARv2uANsA7HwALiwcgqc27PC11dVf8okNJxUtQO8CrwJvAFsWqc3gdyKZUC3BE7fgXMBDO35MfAU+EtEhk22XRoGpgt8CXxsne+kqBboAY+BAxE5b6r9eVPTydIHwH0DRYFixuBICQCmfBcD+nOgp6oHIlI00Y+sQT2zBdw1YAr3Pp99GrqsFe30/1PY548MKD8g11tyLG0Ar1hHxfKxZQVe2PQAGAEnDpQtYMdAEKtnE7htWazshpV9sTbTqiSV4jr5GHhovxURjKoVJ0qgSURUzPeBB65eaVJvNglOVSpERFV1VLHETyoj+90AKuLfVTZ21eBEEERV1QN0hb0i2oRSWbVCvtJ2CCDogsabNmnsXRtwErRXnOKuMg9eanDK9k8LznUYiTa14LTgtOCsKzgyhW5owTFv3PtXag5mC85NSPkM/6eu/7Rqf63We6vck7z8AhFJQlo3BJA06Xd5DCbAKbH6Wc1GK5f54hSmflGh1DvWXu9zLYNWISIaQRKRCcnpECIEbwG7C+qjMulUEEItXffsOSFqUCf9CXxCYAOxdzwgRCdkRnuuFBrgTFWfAM9iVCN3TNsXBKZtm7TsWqRJT4F/a9b1j9WzbwOQA+8nbOs94AdVfQgMc2PY3ga+spHWxPaIWKd+BEbLBOJc+RHwCNizTCKvPL5gixDV+FtEDqPkRBa/SLy8R5CfisjvdZW0DeQv1pmvgVsE4j1VWwur7z1VPcwJDP4tJ/5NpOGsJXNJgI5Nej4s6bZFLPFYbs/pMSVEPrZy+9AtaftVLMF1AFJT8M9V9Yhx0HBRgGKfPzNJjIp8B9jNTWq21816dTZJP0FdJ07BR93TjdMqXzdw4vRMZGyeOcWuHpyufVHWMNXRYS5wOCj1vwPkWclgEtpU6ZXLukpPwmV8ApuWshiDUxYMacGZc1q1qQWnBWcZC/4SJ5VVUAsvKzhlQSkycwpH7VJ+2RfMCft9B+voQiRwH+KmqI3SrBkCo5zxDs51dTxrS4yq7pS8hD7Qywlb53sE3ngdHc/NKZTFIgDtMN6UKTaT+h6cVc7pulIjwGtMJ7sWSQVhK6+4tp4DpzmBuT9peMXKfeCtridtgDRBk6pJ0ClwlllDf2PyIEaqHNMd61AqYL4hHDbZtJGvm9XZfQPgDxH53845Ar5nHP9JmdX02T3TDQuvMK58x+rZc53KEuTY1p7h8KuqhuVbRIaqekAIur1r4ioVc3MREe0C79gyqTav9wnc77Jpn/GRgTjKT7gc1FvUAITABk4G9Zz2HwJHFvXLaijVKC17wLcOnNs2veqAc8fqibrxHPiOECxc1oiN4MwMB0eQkuyjUdXRHCb6Mr5gGYRRqiNF824kSOWvpF7SdYqVm8RuKi8W+SwDa1n03WmXlRmETZz3XNWeQFlCqd9oPqdTql9bcG5QasFpwWnBacFpwWnBacFpwVnCuZvbH1q0/FqDU3V0WlUn8ozyQyap3IwGQ0qrjlVlRo6Lv11g2nUNzi+L8aVsTu//2oMz4jKX/CmBHSwIpP7IgDglcNixo/5oQG7fc8bbYXF1F+sITo9APe4yvuhj37LvYPTWtTTdp23F81GCAQ2GlbImdIpNiQvgZyZ3xVcx/l53xFw+ZOJzLF9Y/RfTpua1lBwXjfyJQIjftWky72DIDJ1SmLQcAo9EpGjqYrNVXWj2OuGo0j7jC808WOX7vOLmhqi7Bva3RyDTn3EDLjSr4qaF8dV3Mca9zThKEfVV34HTt2V8pVfh/Qc1zV/K/uHR/AAAAABJRU5ErkJggg==";
    const string IconDr = "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";
    const string IconDrOff = "iVBORw0KGgoAAAANSUhEUgAAAEcAAABICAYAAACk5ujKAAAFhElEQVR42u2cWZMbNRDHfz2etb2BPSiO4gh3KKAg8Ea+Al89D1S2KIoUFZIlQEIeQpawp69pHtTCsnbsXXtGLnt3VKXyJWukv9St7n9LEhImVUVE4u8EyIAOsGFft4BNwBceAGdWToFT+64I6yurv84kJE6q2gJeA94C3gDa1uk2kFuxDOhG4PQDcM6AoX3/EvgLeC4iw5Rtl8TAdIFvgM+s8606qgV6wANgT0ROU7U/TyVOlj4CbhsoChQzBkciAJjyWQzor4Cequ6JSJGiH1lCPdMBbhkwRfC8MIdpGGQtaWf4n8Lef2JAhQOy2jPH0gbwqnVULL+0rMCJiQfACDgMQOkANwwEsXrawI5lsbIbVvZkbcQqmpUSdPIBcM9+KzwYZSuOn4E2I7xivg3cCeqVlHozJThlqRARVdVRyRI/qYzsdwOo8K/LbOyywfEgiKpqCNAF9opoCqWybIV8oe3gQNA5jTdNaeytDDg1tFcCxV1mHlxrcGL7pwFnFUaiSQ04DTgNOOsKjkyhGxpwzBsP/Ss1B7MB5yqkfIb/U9V/Wra/Vum5Ze5JHj9ARGohrRMBJCn9rhCDCXAiVj+r2GjlPF9ch6lflCj1lrU39LkWQasQEfUgicjEzGnhIgRvA1tz6qOYdCpwoZZu0OC/cVGDKulP4HMcG4g94w4uOiEz2nPhpAGOVfUp8MxHNfKAafsax7RtUh+7ppYz4Aj4t2J9/1g9u4xp0g9rFNsvgZ9V9R4wzI1hewf4NhhprcEmCWnMA+AXYLRIIC4oPwLu28zZSmA3dXBRjRci8jCzmeNZ/CLoWFYxC45Afw7cFZHfqyj7QMHvm3iVDULVXOCI/PdVldym5ivRKBziogTziE/4XoBj4LHVdVB5mTKy3V5TRTq9Ur8BdHJ7E4ZiFXgE/DjntI0DbyMRGSRa1lNypmKYbOU2azajleagjjBrbDesUeoAXS9WeTSt+nWM9hqCcg6crn3QQCmdhDJ+jZIEBmyeTTGYCq5n0hCLxiufwVg04MwQsQachuxqwGnAWebKlV3CFbiuqchwe/BGkSHUvq4rVGjr5Lj9voPIheimIq3XxAgcAqOc8Q7O8MeNOkn2NQSpD/Ry3Nb5Ho5Z80p6W1U7LE5ae8qiv6biNQD6IThhxz4F3ox8jXmH/0xV93G88QvP7Nc4/VOK1ilwlOOY+8NIrPx+36rpAxwj+IOqPqoiXpEOzBMC44MBx55gf4zjkdvUR7Bj9bWB71y/dH9RgII9yR8DNxPNosxE6g8R+d/OeQL8xDj+UwfB7jdWF8A2LuzRWmQFDMq3rJ4tJnfG15V7hsNvnmBHRIaquocLur2Ho06rcDyKI9HexTGNituiv4sL7i2adpk8MjAAnnI+qDevbYOJ/2RQL5i2Q+CJRf2yCkrQr3DbwPcBODsmDlXAuWn1eHBOgbum9GVBEfPgzAwHe5Bq2UejqqOSRmQ16IQYhFFdR4ouu5GgLlO87iW47P9SVzAgXizyus3+6LTLchyiRMGAZe0JlAWU+pXmc1pR/dqAc4VSA04DTgNOA04DTgNOA04DzgLO3aX9oXnLrzU4ZUenVXUizyg/ZJLXzkh4Nn7Zh+4zu1xIwtsFpl3XEPhl/tB9dknvf+XBGXGei/4Cxw4WOFJ/ZEAc4WJFvqP+aIBv46a97kS+WlJ/LSU4PRz1uMX4oo9dy2EHvbeukbhPCwmFRwYGTIaVVlvnBFzOGW4/c8HkvTfFFDDyIMeHTMLsyxdW/9k00VzJmeMbqqq/4gjxWyYmlx0MmaFTCpstD4H7IlKk2vW6rAvNXscdVdplfKFZCFZ8n5ff3OB118Beezgy/RlX4EKzMm5aGF995+/y2mQcpfD6qh+A07dlfKlX4f0HmQpDuVFZV+wAAAAASUVORK5CYII=";
    const string IconRt = "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";
    const string IconRtOff = "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";
    const string IconDd = "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";
    const string IconDdOff = "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";

    void LoadUi()
    {
        try
        {
            tLb = new Texture(Convert.FromBase64String(IconLb)); tLbOff = new Texture(Convert.FromBase64String(IconLbOff));
            tRb = new Texture(Convert.FromBase64String(IconRb)); tRbOff = new Texture(Convert.FromBase64String(IconRbOff));
            tLt = new Texture(Convert.FromBase64String(IconLt));
            tDl = new Texture(Convert.FromBase64String(IconDl)); tDlOff = new Texture(Convert.FromBase64String(IconDlOff));
            tDr = new Texture(Convert.FromBase64String(IconDr)); tDrOff = new Texture(Convert.FromBase64String(IconDrOff));
            tRt = new Texture(Convert.FromBase64String(IconRt)); tRtOff = new Texture(Convert.FromBase64String(IconRtOff));
            tDd = new Texture(Convert.FromBase64String(IconDd)); tDdOff = new Texture(Convert.FromBase64String(IconDdOff));
        }
        catch (Exception ex) { Log("Button icons failed, using simple ones: " + ex.Message); tLb = null; }

        // the font: GreetTauntRob's folder first, then the Pusher folder
        try
        {
            string scripts = Path.Combine(Game.InstallFolder, "scripts");
            string[] dirs = { Path.Combine(scripts, "GreetTauntRob"), baseDir };
            string file = null;
            if (fontFile.Length > 0)
            {
                string f = Path.IsPathRooted(fontFile) ? fontFile : Path.Combine(baseDir, fontFile);
                if (File.Exists(f)) file = f;
            }
            foreach (string dir in dirs)
            {
                if (file != null || !Directory.Exists(dir)) continue;
                string[] all = Directory.GetFiles(dir);
                Array.Sort(all);
                foreach (string f in all)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".ttf" || ext == ".otf") { file = f; break; }
                }
            }
            if (file == null) { Log("No .ttf in scripts\\GreetTauntRob or scripts\\Pusher: using the game font"); return; }
            uiFonts = new System.Drawing.Text.PrivateFontCollection();
            uiFonts.AddFontFile(file);
            if (uiFonts.Families.Length == 0) { Log("Font could not be read: " + file); return; }
            uiFamily = uiFonts.Families[0];
            uiFontOk = true;
            Log("Menu font: " + uiFamily.Name + " (" + Path.GetFileName(file) + ")");
        }
        catch (Exception ex) { uiFontOk = false; Log("Menu font failed, using the game font: " + ex.Message); }
    }

    System.Drawing.FontStyle PickStyle(bool wantBold)
    {
        if (wantBold && uiFamily.IsStyleAvailable(System.Drawing.FontStyle.Bold)) return System.Drawing.FontStyle.Bold;
        if (uiFamily.IsStyleAvailable(System.Drawing.FontStyle.Regular)) return System.Drawing.FontStyle.Regular;
        if (uiFamily.IsStyleAvailable(System.Drawing.FontStyle.Bold)) return System.Drawing.FontStyle.Bold;
        return System.Drawing.FontStyle.Italic;
    }

    // same text rendering as GreetTauntRob: the .ttf drawn into a texture, with a soft shadow
    TextTex RenderText(string text, bool bold, int r, int g, int b, int a)
    {
        const float emPx = 64f;
        const int padX = 6, padY = 4;
        System.Drawing.FontStyle style = PickStyle(bold);
        using (System.Drawing.Font font = new System.Drawing.Font(uiFamily, emPx, style, System.Drawing.GraphicsUnit.Pixel))
        using (System.Drawing.StringFormat fmt = new System.Drawing.StringFormat(System.Drawing.StringFormat.GenericTypographic))
        {
            int lineH = (int)Math.Ceiling(font.GetHeight());
            float textW;
            using (System.Drawing.Bitmap tmp = new System.Drawing.Bitmap(1, 1))
            using (System.Drawing.Graphics mg = System.Drawing.Graphics.FromImage(tmp))
                textW = mg.MeasureString(text, font, 4096, fmt).Width;
            int bmpW = Math.Max(1, (int)Math.Ceiling(textW)) + padX * 2;
            int bmpH = lineH + padY * 2;
            using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(bmpW, bmpH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (System.Drawing.Graphics gfx = System.Drawing.Graphics.FromImage(bmp))
            {
                gfx.Clear(System.Drawing.Color.FromArgb(0, 0, 0, 0));
                gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using (System.Drawing.SolidBrush shadow = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(150, 0, 0, 0)))
                using (System.Drawing.SolidBrush main = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(a, r, g, b)))
                {
                    gfx.DrawString(text, font, shadow, padX + 2.5f, padY + 2.5f, fmt);
                    gfx.DrawString(text, font, main, padX, padY, fmt);
                }
                float capCenter = 0.5f;
                using (System.Drawing.Bitmap hb = new System.Drawing.Bitmap(160, bmpH, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                using (System.Drawing.Graphics hg = System.Drawing.Graphics.FromImage(hb))
                {
                    hg.Clear(System.Drawing.Color.FromArgb(0, 0, 0, 0));
                    hg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    hg.DrawString("H", font, System.Drawing.Brushes.White, padX, padY, fmt);
                    int top = -1, bot = -1;
                    for (int row = 0; row < bmpH; row++)
                    {
                        bool hit = false;
                        for (int col = 0; col < 160; col++) if (hb.GetPixel(col, row).A > 128) { hit = true; break; }
                        if (hit) { if (top < 0) top = row; bot = row; }
                    }
                    if (top >= 0) capCenter = ((top + bot + 1) * 0.5f) / (float)bmpH;
                }
                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    TextTex t = new TextTex();
                    t.Tex = new Texture(ms.ToArray());
                    t.Aspect = (float)bmpW / (float)bmpH;
                    t.CapCenter = capCenter;
                    t.PadRight = (float)padX / (float)bmpH;
                    return t;
                }
            }
        }
    }

    // style: 0 option, 1 option greyed, 2 name, 3 small grey info
    bool UiTextSprite(string text, int style, float right, float cy, float heightNorm)
    {
        if (!uiFontOk || spritesBroken || text == null || text.Length == 0) return false;
        string key = style + "|" + text;
        TextTex t;
        if (!textCache.TryGetValue(key, out t))
        {
            if (textCache.Count > 400) textCache.Clear();
            try
            {
                if (style == 0) t = RenderText(PT(text), false, 235, 235, 235, 255);
                else if (style == 1) t = RenderText(PT(text), false, 125, 125, 125, 190);
                else if (style == 2) t = RenderText(PT(text), true, 255, 255, 255, 255);
                else t = RenderText(PT(text), false, 190, 190, 190, 230);
            }
            catch (Exception ex) { Log("Text render failed: " + ex.Message); uiFontOk = false; return false; }
            textCache[key] = t;
        }
        float xScale = (float)resH / (float)resW;
        float wNorm = heightNorm * t.Aspect * xScale;
        SpriteCmd c = new SpriteCmd();
        c.Tex = t.Tex; c.Aspect = t.Aspect; c.H = heightNorm;
        c.Cx = right - wNorm * 0.5f + t.PadRight * heightNorm * xScale;
        c.Cy = cy + (0.5f - t.CapCenter) * heightNorm;
        uiPending.Add(c);
        return true;
    }

    // Traduccion al espanol del texto del menu
    static string PT(string s)
    {
        if (s == null) return s;
        switch (s)
        {
            case "Comply": return "Obedecer";
            case "Refuse": return "Negarse";
            case "Officer": return "Polic\u00eda";
            case "Handcuffs": return "Esposas";
            case "Sell": return "Vender";
            case "Buyer": return "Comprador";
            case "Bulk Buyer": return "Comprador mayorista";
            case "Rob": return "Robar";
            case "Dealer": return "Traficante";
        }
        return s.Replace("Pick Lock", "Forzar esposas").Replace("Coke", "Coca\u00edna").Replace("Heroin", "Hero\u00edna")
            .Replace("Pills", "Pastillas").Replace(" pills", " pastillas").Replace(" pill", " pastilla").Replace("/pill", "/pastilla")
            .Replace("  wants ", "  quiere ").Replace("Paid $", "Pagado $");
    }

    void UiNativeText(string s, float x, float y, float scale, int r, int g, int b, int a, bool rightAlign, bool centre)
    {
        GTA.Native.Function.Call("SET_TEXT_FONT", 0);
        GTA.Native.Function.Call("SET_TEXT_SCALE", scale, scale);
        GTA.Native.Function.Call("SET_TEXT_COLOUR", r, g, b, a);
        GTA.Native.Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 255);
        if (rightAlign)
        {
            GTA.Native.Function.Call("SET_TEXT_RIGHT_JUSTIFY", 1);
            GTA.Native.Function.Call("SET_TEXT_WRAP", 0.0f, x);
        }
        if (centre) GTA.Native.Function.Call("SET_TEXT_CENTRE", 1);
        GTA.Native.Function.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", x, y, "STRING", PT(s));
    }

    void UiRect(float cx, float cy, float w, float h, int r, int g, int b, int a)
    {
        GTA.Native.Function.Call("DRAW_RECT", cx, cy, w, h, r, g, b, a);
    }

    void UpdateResolution(int now)
    {
        if (now - lastRes < 1000 && now >= lastRes) return;
        lastRes = now;
        try
        {
            RECT r;
            if (GetClientRect(GetForegroundWindow(), out r))
            {
                int w = r.Right - r.Left, h = r.Bottom - r.Top;
                if (w > 100 && h > 100) { resW = w; resH = h; uiAspect = (float)w / (float)h; }
            }
        }
        catch { }
    }

    float IcoAspect(Ico k)
    {
        if (k == Ico.LB || k == Ico.RB) return 91.0f / 52.0f;
        if (k == Ico.LT || k == Ico.RT) return 69.0f / 72.0f;
        return 71.0f / 72.0f;
    }

    float IcoHeight(Ico k)
    {
        if (k == Ico.LB || k == Ico.RB) return 0.026f * UiIconScale;
        return 0.030f * UiIconScale;
    }

    Texture IcoTex(Ico k, bool enabled)
    {
        switch (k)
        {
            case Ico.LB: return enabled ? tLb : tLbOff;
            case Ico.RB: return enabled ? tRb : tRbOff;
            case Ico.LT: return tLt;
            case Ico.DpadLeft: return enabled ? tDl : tDlOff;
            case Ico.DpadRight: return enabled ? tDr : tDrOff;
            case Ico.RT: return enabled ? tRt : tRtOff;
            case Ico.DpadDown: return enabled ? tDd : tDdOff;
        }
        return null;
    }

    Ico IcoFor(Btn b)
    {
        if (b.Trigger) return Ico.RT;
        switch (b.Mask)
        {
            case 0x0100: return Ico.LB;
            case 0x0200: return Ico.RB;
            case 0x0004: return Ico.DpadLeft;
            case 0x0008: return Ico.DpadRight;
            case 0x0002: return Ico.DpadDown;
        }
        return Ico.None;
    }

    float UiColWidth()
    {
        return IcoHeight(Ico.LB) * IcoAspect(Ico.LB) * (float)resH / (float)resW;
    }

    void UiIcon(Ico k, bool enabled, float cx, float cy, string fallbackLabel)
    {
        Texture t = IcoTex(k, enabled);
        float h = IcoHeight(k == Ico.None ? Ico.LB : k);
        if (t != null && !spritesBroken)
        {
            SpriteCmd c = new SpriteCmd();
            c.Tex = t; c.Aspect = IcoAspect(k); c.Cx = cx; c.Cy = cy; c.H = h;
            uiPending.Add(c);
            return;
        }
        // simple pill, same as GreetTauntRob's fallback
        float w = h * 1.4f / uiAspect;
        int body = enabled ? 95 : 55, txt = enabled ? 255 : 140;
        UiRect(cx, cy, w, h * 0.8f, body, body, body, 235);
        UiNativeText(fallbackLabel, cx, cy - 0.011f, 0.28f, txt, txt, txt, 255, false, true);
    }

    // [text] [drug icon] [button]  -  GreetTauntRob's row
    void UiRow(float cy, string label, bool enabled, Ico kind, string buttonLabel, Texture drug)
    {
        float colW = UiColWidth();
        float iconCx = UiEdge - colW * 0.5f;
        float textRight = UiEdge - colW - 0.008f;
        if (drug != null && !spritesBroken)
        {
            float dh = UiOptionH * 1.05f;
            float dw = dh * (float)resH / (float)resW;
            SpriteCmd c = new SpriteCmd();
            c.Tex = drug; c.Aspect = 1f; c.H = dh; c.Cx = textRight - dw * 0.5f; c.Cy = cy;
            uiPending.Add(c);
            textRight -= dw + 0.006f;
        }
        if (!UiTextSprite(label, enabled ? 0 : 1, textRight, cy, UiOptionH))
        {
            int c = enabled ? 235 : 125, a = enabled ? 255 : 170;
            UiNativeText(label, textRight, cy - 0.0717f * UiOptionScale, UiOptionScale, c, c, c, a, true, false);
        }
        UiIcon(kind, enabled, iconCx, cy, buttonLabel);
    }

    void UiInfo(float cy, string text)
    {
        if (!UiTextSprite(text, 3, UiEdge, cy, UiInfoH))
            UiNativeText(text, UiEdge, cy - 0.0717f * UiInfoScale, UiInfoScale, 190, 190, 190, 230, true, false);
    }

    // divider + name (+ LT icon), placed under the last row exactly like GreetTauntRob
    void UiName(float lastRowY, string name, bool lt)
    {
        float divY = lastRowY + UiStep * 0.62f;
        UiRect(UiEdge - 0.075f, divY, 0.15f, 0.0016f, 255, 255, 255, 190);
        float nameY = lastRowY + UiStep * 1.22f;
        float colW = UiColWidth();
        float nameRight = lt ? (UiEdge - colW - 0.008f) : UiEdge;
        if (!UiTextSprite(name, 2, nameRight, nameY, UiNameH))
            UiNativeText(name, nameRight, nameY - 0.0717f * UiNameScale, UiNameScale, 255, 255, 255, 255, true, false);
        if (lt) UiIcon(Ico.LT, true, UiEdge - colW * 0.5f, nameY, "LT");
    }

    static string TitleCase(string s)
    {
        if (s == null || s.Length == 0) return s;
        string[] w = s.ToLowerInvariant().Split(' ');
        for (int i = 0; i < w.Length; i++)
            if (w[i].Length > 0) w[i] = char.ToUpperInvariant(w[i][0]) + w[i].Substring(1);
        return string.Join(" ", w);
    }

    string StashLine()
    {
        return "Coke " + stash[COKE] + "/" + maxCarry[COKE] + "g     Heroin "
            + stash[HEROIN] + "/" + maxCarry[HEROIN] + "g     Pills " + stash[PILLS] + "/" + maxCarry[PILLS];
    }

    // called every frame from the tick
    void DrawUi()
    {
        if (!inited) return;
        UpdateResolution(Game.GameTime);

        List<string> labels = new List<string>();
        List<bool> enabled = new List<bool>();
        List<Btn> buttons = new List<Btn>();
        List<Texture> drugs = new List<Texture>();
        List<string> info = new List<string>();
        string name;
        bool lt = false;
        float lift = 0f;      // how far the menu is moved up the screen

        if (copChoice)
        {
            if (choiceKind == 2)
            {
                // found: lock on to him (LT) for the choice
                if (!aimHeld) return;
                labels.Add("Comply"); enabled.Add(true); buttons.Add(btn[B_D0 + HEROIN]); drugs.Add(null);
                labels.Add("Refuse"); enabled.Add(true); buttons.Add(btn[B_D0 + PILLS]); drugs.Add(null);
                name = "Officer"; lt = true;
            }
            else if (choiceKind == 3)
            {
                labels.Add("Pick Lock  " + pickCount + "/" + lockpickPresses); enabled.Add(true); buttons.Add(btn[B_COMPLY]); drugs.Add(null);
                name = "Handcuffs";
                lift = 0.11f;     // in the squad car: above the game's vehicle / area / street names
            }
            else
            {
                labels.Add("Refuse"); enabled.Add(true); buttons.Add(btn[B_ROB]); drugs.Add(null);
                labels.Add("Comply"); enabled.Add(true); buttons.Add(btn[B_COMPLY]); drugs.Add(null);
                name = "Officer";
            }
        }
        else if (scene != S_NONE || mode == M_NONE) return;
        else if (mode == M_BUYER)
        {
            labels.Add("Sell"); enabled.Add(true); buttons.Add(btn[B_SELL]); drugs.Add(null);
            name = "Buyer"; lt = true;
        }
        else if (mode == M_SELL)
        {
            if (curOffer == null) return;
            for (int i = 0; i < 3; i++)
            {
                string text; bool ok;
                if (curOffer.Bulk)
                {
                    ok = curOffer.Units == 0 && curOffer.Qty[i] > 0 && stash[i] >= bulkAmount;
                    text = DrugName[i] + "  " + Amount(i, bulkAmount) + "  $" + bulkSellPrice[i];
                }
                else
                {
                    int left = curOffer.Qty[i] - curOffer.Sold[i];
                    ok = left > 0 && stash[i] > 0;
                    text = left > 0 ? DrugName[i] + "  $" + sellPrice[i] + "/" + Unit(i) + "  wants " + left : DrugName[i] + "  $" + sellPrice[i] + "/" + Unit(i);
                }
                labels.Add(text); enabled.Add(ok); buttons.Add(btn[B_D0 + i]);
                drugs.Add(IconFor(i, curOffer.Bulk ? bulkAmount : 1));
            }
            name = TitleCase(sellTitle);
            lt = ltWasHeld;
            if (curOffer.Paid > 0) info.Add("Paid $" + curOffer.Paid);
            info.Add(StashLine());
        }
        else if (mode == M_DEALER)
        {
            if (curDealer == null) return;
            for (int i = 0; i < 3; i++)
            {
                bool ok = CanBuy(curDealer, i, hudMoney);
                labels.Add(DrugName[i] + "  " + Amount(i, buyStep[i]) + "  $" + DealerPrice(curDealer, i));
                enabled.Add(ok); buttons.Add(btn[B_D0 + i]); drugs.Add(IconFor(i, buyStep[i]));
            }
            if (OwnBuyerUi() && !curDealer.Door && OrderTotal() == 0) { labels.Add("Rob"); enabled.Add(true); buttons.Add(btn[B_ROB]); drugs.Add(null); }
            name = "Dealer";
            lt = dealerLockOn;
            info.Add(StashLine());
        }
        else return;

        int n = labels.Count;
        float y0 = UiTop + (3 - n) * UiStep - lift;
        for (int i = 0; i < info.Count; i++) UiInfo(y0 - UiStep * (0.95f + 0.75f * (info.Count - 1 - i)), info[i]);
        for (int i = 0; i < n; i++)
            UiRow(y0 + i * UiStep, labels[i], enabled[i], IcoFor(buttons[i]), buttons[i].Label, drugs[i]);
        UiName(y0 + (n - 1) * UiStep, name, lt);
    }

    void ShowPickupIcons(bool[] which)
    {
        bool any = false;
        for (int i = 0; i < 3; i++)
        {
            // not loaded at start (file added later, or it failed): try again now
            if (which[i] && pickupIcon[i] == null)
            {
                pickupIcon[i] = LoadTexture(PickupIconFile[i]);
                if (pickupIcon[i] != null) pickupFade[i] = LoadFaded(PickupIconFile[i]);
            }
            pickupShow[i] = which[i] && pickupIcon[i] != null;
            any |= pickupShow[i];
        }
        pickupStart = Environment.TickCount; pickupOn = any;
    }

    void DrawPickupIcons(GraphicsEventArgs e)
    {
        if (!pickupOn) return;
        int start = pickupStart;
        int t = Environment.TickCount - start;
        int total = pickupFadeIn + pickupHold + pickupFadeOut;
        if (t < 0 || t > total) { pickupOn = false; return; }
        float a;
        if (t < pickupFadeIn) a = t / (float)Math.Max(1, pickupFadeIn);
        else if (t < pickupFadeIn + pickupHold) a = 1f;
        else a = 1f - (t - pickupFadeIn - pickupHold) / (float)Math.Max(1, pickupFadeOut);
        a = Math.Max(0f, Math.Min(1f, a));

        List<int> icons = new List<int>();
        for (int i = 0; i < 3; i++) if (pickupShow[i] && pickupIcon[i] != null) icons.Add(i);
        int n = icons.Count;
        if (n == 0) return;
        int idx = (int)Math.Ceiling(a * PickupFadeLevels) - 1;
        if (idx < 0) return;                                  // fully faded out this frame
        if (idx >= PickupFadeLevels) idx = PickupFadeLevels - 1;
        e.Graphics.Scaling = FontScaling.Pixel;
        float W = Game.Resolution.Width, H = Game.Resolution.Height;
        float size = pickupSize * H, arc = pickupArc * H;
        float cx = pickupX * W, cy = pickupY * H + arc;      // centre of the minimap's curve
        for (int k = 0; k < n; k++)
        {
            float deg = n == 1 ? 0f : (n == 2 ? (k == 0 ? -pickupSpread : pickupSpread) : (k - 1) * pickupSpread);
            double rad = deg * Math.PI / 180.0;
            float px = cx + (float)Math.Sin(rad) * arc, py = cy - (float)Math.Cos(rad) * arc;
            int i = icons[k];
            if (pickupFade[i] != null) e.Graphics.DrawSprite(pickupFade[i][idx], px, py, size, size, 0f, Color.White);
            else e.Graphics.DrawSprite(pickupIcon[i], px, py, size, size, 0f, Color.White);
        }
    }

    void OnDraw(object sender, GraphicsEventArgs e)
    {
        try { DrawPickupIcons(e); } catch (Exception ex) { pickupOn = false; Log("Pickup icons: " + ex.Message); }
        if (spritesBroken) return;
        List<SpriteCmd> cmds = uiCmds;
        if (cmds == null || cmds.Count == 0) return;
        try
        {
            foreach (SpriteCmd c in cmds)
            {
                float h = c.H * (float)resH;
                float w = h * c.Aspect;
                e.Graphics.DrawSprite(c.Tex, c.Cx * (float)resW, c.Cy * (float)resH, w, h, 0.0f);
            }
        }
        catch (Exception ex)
        {
            spritesBroken = true;
            Log("Sprite drawing failed, switching to plain text: " + ex.Message);
        }
    }
}
