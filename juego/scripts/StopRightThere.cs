// StopRightThere.cs  -  speeding stops for GTA IV / TLAD / TBoGT
// (.NET ScriptHook, single .cs file)
//
// Install:
//   scripts\StopRightThere.cs
//   scripts\StopRightThere\PricedownBl-Regular 900.ttf
//   scripts\StopRightThere\replies.txt
//   scripts\StopRightThere\IV\accept\ACCEPT1..10.wav            (one at random when you accept)
//   scripts\StopRightThere\IV\acceptafter\ACCEPTAFTER1..2.wav   (50% chance, about 5 s after accepting)
//   scripts\StopRightThere\IV\deny\DENY1..10.wav                (one at random when you deny)
//   scripts\StopRightThere\IV\AcceptArrest\ACCEPTARREST1.wav    (you accept but have under $100)
//   scripts\StopRightThere\IV\Caught\CAUGHT1..3.wav             (always, 2 s after the lights come on)
//   (same folders inside TLAD and TBoGT when those are set up)
//
// A log is written to scripts\StopRightThere.log - send it along if something misbehaves.
//
// The script never spawns police. It only uses cops the game already put on the street.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GTA;
using GTA.Native;

public class StopRightThere : Script
{
    // ===================== VOLUME =====================
    // Same control as StrangerSystem: 1000 = the wav's own loudness, 200 = 20 %, 0 = silent.
    static readonly int VoiceVolume = 200;

    // ===================== ANIMATIONS =====================
    // officer at your car window (hand on the car), looping
    const string CopCarAnimSet = "amb@bridgecops";
    const string CopCarAnim = "car_chat_outside";
    // officer at a truck window, looping
    const string CopTruckAnimSet = "cop";
    const string CopTruckAnim = "copm_licenseloop_truck";
    // where he stands at the driver's door, in metres from the centre of your car.
    // Hand floats / clips: change CopDoorGap. Too far forward / back: DoorForwardOffset.
    // Facing the wrong way: CopCarHeadingOffset (degrees added to "facing the car").
    // the hand-on-car animation is held still this long after it starts, so his hand stays on the car
    // while he talks instead of moving about (0 = let the animation play)
    static readonly int CopCarHoldAfterMs = 700;
    const float CopDoorGap = 0.50f;             // metres between the side of your car and where he stands
    const float DoorForwardOffset = 0.35f;
    const float CopCarHeadingOffset = 0f;

    // you, sitting in a car or truck: the intro once, then the loop for as long as the officer is at the window
    const string PlayerIntroSet = "cop";
    const string PlayerIntroCar = "plyr_licenseintro_ncar";
    const string PlayerLoopCar = "plyr_licenseloop_ncar";
    const string PlayerIntroTruck = "plyr_licenseintro_truck";
    const string PlayerLoopTruck = "plyr_licenseloop_truck";
    const float TruckMinHeight = 1.9f;          // vehicles taller than this (metres) use the truck animations

    // paying in a car
    const string PlayerOutroSet = "cop";
    const string PlayerOutroAnim = "plyr_licenseoutro_ncar";

    // paying on a motorcycle
    const string BikePaySet = "amb@tollbooth";
    const string BikePayChopper = "toss_money_chopper";
    const string BikePayDirt = "toss_money_dirt";
    const string BikePayFreeway = "toss_money_freeway";
    const string BikePayScooter = "toss_money_scooter";
    const string BikePaySport = "toss_money_spt";
    // which bike uses which one (model names). Anything not listed: chopper in TLAD, sport elsewhere.
    static readonly string[] ChopperBikes = { "ZOMBIEB", "HELLFURY", "ANGEL", "DAEMON", "DIABOLUS", "HEXER", "INNOVATION", "LYCAN", "NIGHTBLADE", "REVENANT", "WAYFARER", "WOLFSBANE" };
    static readonly string[] DirtBikes = { "SANCHEZ" };
    static readonly string[] FreewayBikes = { "BOBBER" };
    static readonly string[] ScooterBikes = { "FAGGIO", "FAGGIO2" };
    static readonly string[] SportBikes = { "NRG900", "PCJ", "BATI", "BATI2", "DOUBLE", "HAKUCHOU", "HAKUCHOU2", "AKUMA", "VADER" };

    // officer taking the money (car and motorcycle)
    const string CopTakeSet = "amb@beg_standing";
    const string CopTakeAnim = "take_obj";
    // hands don't meet: start his take this many ms AFTER your give (0 = same moment)
    static readonly int CopTakeDelayMs = 1000;

    // officer writing a ticket (motorcycle stops): plays once, then is held
    // part-way through so the ticket stays in his hand instead of the whole cycle repeating
    const string WriteSet = "amb@bouncer_idles_b";
    const string WriteAnim = "lookaround_a";
    const float WriteHoldAt = 0.5f;             // 0..1 - where in the animation he is held
    static readonly int WriteHoldFallbackMs = 2500;
    const float PaperGoneAt = 0.9f;             // 0..1 - point in the animation where the paper is in his pocket
    static readonly int PutAwayMaxMs = 4000;    // longest the put-away may take before the stop moves on
    static readonly bool WriteUpperBodyOnly = true;   // it is a walking animation: only his arms and body play it, his feet stay put

    // second officer: radio once, then the standing idle
    const string PartnerRadioSet = "cop_search_idles";
    const string PartnerRadioAnim = "idle_answer_radio_a";
    static readonly int PartnerRadioMaxMs = 8000;
    // ticket in the writing officer's hand at a motorcycle stop, while he has it out ("" = no prop)
    const string PaperModelName = "cj_proc_paper4";
    static readonly bool PaperInLeftHand = false;   // false = right hand
    // where the ticket sits in his hand. Set PropTuning = true to line it up IN GAME while a cop is holding it:
    //   numpad 4 / 6 = left-right,  8 / 2 = forward-back,  7 / 9 = up-down      (1 cm per press)
    //   hold numpad 0 with the same keys = turn it instead                       (5 degrees per press)
    // The six numbers are shown on screen and written to the log; put them here (or send them to me) when it looks right.
    static readonly bool PropTuning = true;
    static float PaperOffsetX = 0.05f;
    static float PaperOffsetY = 0.02f;
    static float PaperOffsetZ = 0.0f;
    static float PaperRotX = 0.0f;
    static float PaperRotY = 0.0f;
    static float PaperRotZ = 0.0f;
    // radio in his right hand, only while the radio animation plays ("" = no prop)
    const string RadioModelName = "amb_walkietalkie";
    const float RadioOffsetX = 0.05f;
    const float RadioOffsetY = 0.02f;
    const float RadioOffsetZ = 0.0f;
    const float RadioRotX = 0.0f;
    const float RadioRotY = 0.0f;
    const float RadioRotZ = 0.0f;
    const string PartnerIdleSet = "amb@default";
    const string PartnerIdleAnim = "bouncer_default";

    // your in-vehicle animations are played as a "secondary" animation so you can still drive off.
    // If you never see them, set this to false.
    static readonly bool PlayerAnimsAsSecondary = true;

    const AnimationFlags LoopFlag = AnimationFlags.Unknown05;

    // ===================== SETTINGS =====================
    const int NoCashArrestMs = 1000;        // accept without enough money: the officer moves in this long after your line has finished
    const int DenyArrestMs = 800;           // after you DENY: time before the officer goes to pull you out
    const int DenyWaitMs = 3000;            // after you deny / can't pay: time before the officer goes for the arrest
    const int AcceptAfterMs = 5000;         // after you accept: earliest your "acceptafter" line can play
    const int CaughtDelayMs = 2000;         // your "Caught" line plays this long after the lights come on
    const int CaughtPct = 100;              // chance of a "Caught" line
    const int AcceptAfterTakeMs = 4000;     // your "acceptafter" line plays this long after the officer takes the money
    // one line that plays most of the time: scripts\StopRightThere\TLAD\Deny\DENY7.wav, 80%.
    // The other 20% picks one of the remaining files in that folder at random.
    const string FavouredEpisode = "TLAD";
    const string FavouredFolder = "deny";
    const string FavouredFile = "DENY7.wav";
    const int FavouredPct = 80;
    const int ArrestPullMs = 1800;          // how long his drag-you-out move is allowed to run before the vanilla police take over
    const int ArrestForceOutMs = 3500;      // deny / can't pay: if he has not dragged you out by then, you get out yourself and are arrested
    const int ArrestTimeoutMs = 10000;      // deny / can't pay: how long the officer gets to pull you out before the vanilla police take over anyway
    static readonly bool BlockTalkGestures = true;   // officers do not wave their hands while talking at your vehicle
    const int AcceptAfterPct = 50;          // chance of an "acceptafter" line
    static int StopChancePct = 50;          // % chance that a squad car which sees you speeding actually pulls you over (config.ini: StopChance)
    static float SpeedLimitMph = 45f;       // faster than this near a squad car = speeding (set in scripts\\StopRightThere\\config.ini)
    const int SpeedingHoldMs = 700;         // must be over the limit this long (ignores 1-frame spikes)
    const float WitnessRadius = 40f;        // metres - how close the squad car must be to "see" you speeding
    const int TicketCost = 100;
    const float ParkGapMeters = 0.91f;      // 3 feet between your rear bumper and the squad car (following and parked)
    static readonly bool DirectSteering = false; // false = the squad car is driven by the game's traffic AI only (old behaviour)
    const float SteerMaxPlayerSpeed = 11f;  // metres/second (about 25 mph): below this the squad car follows your exact line
    static readonly bool ChaseYourCar = false;      // true = while he is further away the game's driver is told to go after YOUR CAR (not a spot on the map)
    const float ChaseRange = 8f;                    // ...until he is this close; then he pulls up to the spot behind your bumper
    static readonly bool UTurnToYou = true;         // true = a squad car that is pointing away from you turns round on the spot to come after you
    const int FlipDelayMs = 3000;                   // the oncoming squad car must be out of your sight this long before it is put behind you
    const float CatchUpGapMeters = 45f;             // further back than this, out of view and not gaining = put behind you
    const float PlacedBehindMeters = 28f;           // how far behind you it is put (was 15)
    const float PlacedExtraSpeed = 1f;              // metres/second faster than you he rolls after being put behind you (was 3)
    const float UTurnRadius = 6.5f;                 // metres: how tight that U-turn is
    static readonly bool VanillaChase = false;      // true = until he is close and pointing at you, the squad car is driven by the game's own 1-star police pursuit
    const float VanillaTakeover = 5f;               // metres: inside this, and pointing at you, the script has him pull up behind your bumper
    static readonly bool LineUpBehindYou = false;   // true = the script steers the squad car the last metres to park it straight behind you
    const float SteerRange = 15f;           // ...once it is within this many metres of you
    const float SteerTurnRate = 75f;        // degrees per second the squad car may turn while lining up
    const float PullOverSayDistance = 18f;  // the "pull over" line is said once the squad car is this close (metres)
    const int PullOverMs = 15000;           // time to pull over once the lights come on
    const int SecondChanceMs = 10000;       // time to stop again if you drive off during the officers' wait
    const int WaitBeforeExitMs = 3000;      // officers sit in the car this long once both are stopped
    const int TicketAnswerMs = 15000;       // hidden timer to accept / deny once the prompt is up
    const float LoseDistance = 220f;        // outrun them by this much and the vanilla 1-star takes over
    const int SpeechGuessMs = 3500;         // assumed length of a vanilla cop speech line
    static readonly bool ShowStarDuringStop = true;   // false = no star during a speeding stop until you flee / deny

    // keyboard fallbacks (controller uses LB = deny, RB = accept)
    static readonly bool EnableKeyboardFallback = true;
    const int VK_DENY = 0x4A;     // J
    const int VK_ACCEPT = 0x4B;   // K

    static readonly string[] PoliceCarModels = { "POLICE", "POLICE2", "POLICE3", "POLICE4" };
    // Officer speech: the game is first asked to use the cop's own voice. Only if he stays silent
    // is the line forced with one of these voices (picked once per officer; it may not match his face).
    static readonly string[] CopVoices = { "M_Y_COP_WHITE", "M_Y_COP_WHITE_02", "M_Y_COP_BLACK", "M_Y_COP_BLACK_02", "M_Y_COP_HISPANIC" };
    static readonly string[] TrafficCopVoices = { "M_Y_COP_TRAFFIC_WHITE", "M_Y_COP_TRAFFIC_BLACK", "M_Y_COP_TRAFFIC_HISPANIC" };
    const string TrafficCopModel = "M_Y_COP_TRAFFIC";


    const string FolderName = "scripts\\StopRightThere";
    const string FontFileName = "PricedownBl-Regular 900.ttf";   // not found: the first .ttf/.otf in the folder is used
    static readonly bool EnableLogging = true;

    // ---- UI layout: identical numbers to StrangerSystem ----
    const int HudFont = 0;
    const float OptionTextHeight = 0.026f;
    const float NameTextHeight = 0.030f;
    const float OptionScale = 0.24f;
    const float NameScale = 0.27f;
    const float TextYOffset = 0.0f;
    const float RowStep = 0.029f;
    const float IconScale = 0.62f;
    const bool SpriteOriginIsCenter = true;
    const float RightEdge = 0.975f;
    const float RightTop = 0.836f;
    // =====================================================

    static readonly string IconDl =
    "iVBORw0KGgoAAAANSUhEUgAAAFsAAAA0CAYAAADllEQWAAAH/klEQVR4nO2cXYxdVRXHf+vMmUGKWGyklOrAQKVKGtOIXzGExD74gvqAMZj4ID6gQFBEGzUxaDSo0cQoiK2aGNNH4oPGrygmBCIYNcQHTGkI8aOUGEQlprRl2rkfy4e11j37nrmfM3ff9k7nn5zsc8/eZ3/8z9prr7323lc4y1DVov4IQES0T3oBLgF2Aq8CtgFSS7YI7AZaPeJSnAIaSZoW8DRw2p/9VUSejnqKSHukRvXBoIpkgZM152WriDQHpH0D8DZgL3Ax9iGWMIKXgFcD8xmr+zzwS+CAiDzpgqH9BGEYspPt5EZZIiKtWvwO4DJgAbgBuBmTWsEI3jqkiF4faz3tSoksPTwJ3AY8yJCeNwhZyE6kV3uQuwB8ELgSI/L9wNVDsmxSkSA9rlxoY6oles99wKejzHHVykQrGiSnqkFVLwFeAdwO3Ai8Eri29mp8EKmFWeq5BrT9KoFvich+sPaOI+HrboTrsV7q4SZgF3A38Nr6a1QD0xxQHyTPVbSxuj6ASXgbRpfwNZPtUlyKSMN/F8AVwBeB12P6t+sVr9w0un8uRBvmgIMicieMLuFjN9hJlviaqno5cD1wD2ZuXehJW1RdbxaJHYQWRvj3gE9QDZoDJXxkElxyi9DHqnop8FngvcAbaxXBK7NRoX4VwEHg4xExSMJHIjs16FV1CbgD+DCww5M0qcjdaFLcD6lKOYBJONCf8IHEqOoc0BYRVdXLgI8Cn8MsCjCSC2ZngJs0Ugk/ANwVEb1USl+yVbUUkaYTfgvwecy6gO6B7nxHKuEPiMhd0HvQXEVWzPhcmvcA3wDe49ENNuaANwnEoPldzNxdNWgOkuxbga8C23F7ksmoC6WaEQpVNywHvdQnn8Yayl9LWaMglfD7ReRu6JbwLrLd4rgU+AJwpz+OL7aREJOTHAi+voNZa0183OuQrapzItJS1Z8D76NyT06qUiHJ/8Hs0xNeqRZwDTb4SpJuWD7Hga+MUX7k/SbMkhpWzlqhWJtK4FMicl+Mf1UKGwhR1cOq2vJrkoj8jtR92Kr6liS+PSSfiH92zWyoftLzaIxQ3lrQ9rxPqeoNXmZR+o24VL8G2IJJ87oc5QNQAjtV9QXMm9bAVNe4mFPVui87fC11hLRZIpH7va33UFlWk0TUYwvwoKpeAyyHhEW4F/Mlxws5oEDDfSoR9l1AGJSPiDRq14qILPe4TgOtKEutZ30NWxwo6PZhTwqCtWsHcLuIaJlEgDmQLsCkYGYmKt4zVVW3AvvotnQEeF5E/gQdk1ZEZFlVDwL3UlkRE68axuPbVVXqZC96OGsWSIHVeTfw0x7xK6p6h4j8yKW6UFUFjnh8LrIjz33AtXXpPZ2hwGmiAawAyx6uYG1awPzP0K0eX85cnwJTJduBXaXajDEGw+09KjRLKDBie+FMch/+nncl7+WEAqaz3RLZArx5SoXnQhN4iW4vZBN4Ebg1EolI2/X7x/xRbpUpUPk5FFu62kOl1GcJ0TOfAd5JZd2UwHER+Sd0/D5ttTnFIWwrRM7ZJFRaop06lbZ64bM2OHb8x27iHanHO8khVBcBPwHejX2UHH6STtEYly8BL6Rf9DR57M2pQlWL+pVECzZ4/hj4O0Z0zjZHj/sbcDQ16K9idgfGDkSk3ePSuMcGxx9i5tiTVBKfA0H2UyLyYvrVr4v6Zio4G7Ty6+xR1WOq+g8Pj6nqUQ/vTdLNi8gxzLN5AiMlp4S/DN1T1X0eng2yYxPMenEBNjFb8nAR23m1iPlBbnL9rk7474GfYXp1LS6DYQguT4GtlqsPILv6v5MdTcwObo5wDZLA2C624mFcyx7XZdp6u49PtikdKDYmtIBH8B/hL8hU5khYwKyE9aLApLRuTcVelt94mG4l/lfybg78F3gUnGy1naQXDnghF0J1/AX4DLZrtd+e6vCi3Qa8jm4Jj/tTwGFMkkNFzgPPYtt+H0vcyfMuZJcndclh8jZF5ARUNubVmO912gj7+N/AN0d6QXUv8IH0WbKo+gzwVrr1v4jIir8b3sE5oKW2o3YveSY24XF83NVVh+zrMcnOOZsK6QufsgCSTDiG+ZVXLZnp6lMLiMiZ+jMvo7RblWR/4g+wtueYyEVbDsUHDrKv8nAafuwzwEm3CmL1JFamh0JVw2rQkXePWlmdlXhfpfkS8BHyz5iPxU14/aYxPQ+J3AbsVtXnMH066sgsGDGxhFaq6hUMN1XDq7mAqZhFzCkVZ25ytD3yfRh4LnwypYv4xUnFciF6zBJmFZxk/F4UZ2rAjoY8xmh1Dh9Fuk8814AIFdl/EJETPhg3SlXdhn1lmN6EZufwJEMxh+0HHwehTkryjk3zmEX0hD9r44VeSTWhmRbZ65ktpiSNm4/Qf3FhUggX9VHgF2o7gFtgZO/AzhPmdjemmJRUnat+d8VUiKYWU5Fcm5gMCqwHfdt/t9OIbWejRhsUYVk9hamRLhTYDApm0LV6DiLmAIdE5KTaHr+OaVuQ9zjy+YQ4rPU/4PFeCQpseWjml8POAYRu/p2I/NFt6y4feYHpl00Vsn7Enx9833+36gkKzPD+M/lWK84HBLGHgd+6s2vVHKAQkePAfqoN3A021co4iBNzAux3knua0qLViYMbsSX+i5JMNjEcJaavvw58GT/Z3OsspEBntbmhqtdhK84fwv5JYRPD8WtsFehXwxJ2OeKTU7zvwFyZuc6dzDqClybwUCwO1P+Zoo7/A+LYPS57yB97AAAAAElFTkSuQmCC";
    static readonly string IconDr =
    "iVBORw0KGgoAAAANSUhEUgAAAFsAAAA0CAYAAADllEQWAAAJVklEQVR4nO2bbYycVRXHf2dmdlsBkba2sAWxCNYYKggSFUma+FITP5AYDdGgIWpC/KAhNvGF4AcNMb5FkwJFG4OJJmjUIDTRFAMqlKAWqS+REgVaC4VuIdpSCq3dndn5++Hcs3Nndmbnpc/sbmH/yZNn5rn3Oc99zj333HP+9z7GCUCSAWZm9fT/CmA5UAfOAdYBE4D1Ixb4O3C45XodGAdeAMbNbKJLu2jzXJmZ+mhLoehHCU2QZNFwSVcDHwauBEYKals7HAYOAU8B/wUOAqV07RHgATN7ZrY2AxW8Q6fmWvEDKVtSyczqkk4H7gTemxXXst8l/MX6fkSH65Uu9x0CjqX7Hwe2An8BjgKHWjtCUjnVFcCwld+3siWVzWxK0nJgG/AOYApvcHkQmf08vs1Bema3jhgHfgPsBZ4GtprZC03CpRJuIEOx+r4UE65D0quB3wOX4YouF92wAdCqHLWcW9v4BO6W7gV+DBw0s4PTN0sjQK1Ipfes7MyiVwC/Bt4JTAKjRTVmyKhnRwW34ByHgM3Ao8DdZvZiFIS7iUBgUPSk7MxHnwHcB7yVhWPRgyB3QyVm6uGvuNJvBv5mZlMAkkaB6qDW3lXZmUUvw33e2/FJsJuPPNkg3ICg8W6TwDPAV4DtZvY0TPt2+rX0WZXd4jruAS4Fqgw3vFsICKXnI3cv8CvgG2b2LICkClDvVekdld0S3t2HK/pkdh2DIiw+rP054OfAzWa2BxpG2U1QW2VnFr0SD+8u4+SaDIeBOq70GNU14OvAFjM7AN2VPkPZmUUvA+4HLuKVadGdkE+sAP8GvgfcYmaTkipmVmt3Y5OyO1j0K8FHD4pcN9uBL5nZQ62cUWBa2ZlFvxa36AsZTtSh1MheEdnhbJN5DR/mRn/0QLt4u1+EQks4VfBlM9vUrqJBk6JXAL/F4+iFFt7VOXHFDFNu7mp/CVxvZrtzws4yOvJ0fChczHBch/DOfRG4sYf6YaUV4LPA2S1yoPGCP8MJpzKNsK0X2VfheUNRCs8jlz+Z2bvCkL1UKkkqS3pYjpqGg3o6j/f9BtKYpO+kttUzWZPp/MGBNCOdJulWSVPpKAIha5+kc0PHAJXkPq5l7ibDkpzkyWG0j3amcE7iAPB5SXuAW5hJ3S5LMkdozAfB4LUirM/M7CXgM5JeBXySYlynpeNM4E3APmIkSlolaWeylmpBvdsOYY3P9tNySSYffSPp/+ZkOTVJE0nmNamsJ0MJS5NUSfLHJD2m4iw89Lgxb1cFnwzfxjxMiGpQtqcCG2jmpyfNbFsqB6jJ55cvAh8CxmheqGiV+WbcssLHx3m3me1K9WqSRszsgHzUrE0yi4pQluYXK8Bbsheca4QSzgbuai2UdC/wKWB/qmtmdkzS7cAXOsgs4wq7FtjYpvygpCuBHUrUaerEx4APUIweQsaSeBXwHvxYujCMsKpX1HBfO5HOVZwe2ABsSqFTvjLzgx5kHkkyjqfzJB4HrwA+kskM/C+di1T2hdkIsm7JwlzB8MktD8GEd8K7wdcH1QhTezGMEWZyOfE/IiIDSkkZF6VrRYSBcf9a4AzgecCKyKCKQqyigCthCm/b3jZ123IPLTiGL/RWabir48Ae4KepTgWoSnoDcAnNnMeJIGSsA1bhyqaCJxnzDWNmqBaT9SZoiiAMJ8c6IZKa7+Mc/FG8c8rAfjM7Mv3QtPdE0leB1RRLuIWs6Qipgm+IuQK3qrlm9sJnHgMeouGXl+Dx6U14VkvKB8rpfEMngdOpsdkhfF2x+YHeWSX8fd+IrztuYHjM5rQBVfA0d16QKWZcvpsK0lBupSlTiFaVtB6PoIJ4aotMqfkk2Lrivg/4BXAuHiYWyb9E29YD/yAJjiRj3rZlJYTPVig6hWZBlIVv/SGNia6jss1MZjZlZvXsUDrqqaMnzOw2fJPRP3EdnNAKehtcks7Ke3Feo5JMEcquTSVfLUnvA/4IXJCK21pg1kHXyfmJJ9M5//3pVM/SiNmPp+vD2GS0Ln5U8G1a+/ChNCwac1akdHYN7jdH8QikSsr6kp++FOcbeuVvlgOv61C2Bd8bsi+l1aVE+u/ClZMzi4Mi7h+LCxFa7Uz/ix5CsyIiDDyDfABfWN4OfDNoyVjTM7Nv4/NLxOPdMIl3XjWdp7L/deC8VC84fcOpWnqU3ysU7xlc63+ioMCH9IMKcBY+ulYBn5P0/pTIBAVseAp+nOZsshOW0Ai9ytkxgiv40VQvl3OsoPfJcQqw0swUsWzs7pyvBCcmprCoMr5yfU9qU4Rlu4A7gKvpvEgQytsF7Ej1IqkZBf6MW/BhmM5M47w23VtUyi7gNXhofae3Tlor6fnkv4IKLRozKFY1EpXzU1nQm9X0+/pUXs7qrpZ0JMk7nuq3pVjlFGp+zPD1akyoKyUdbWnriSIWYm4EJ/LNzB4HXiqgN3tCprySGtt0c8T/6ySNkTJM+TaBceAGmq2vpLSiLeenLb1XreWoKnHj6RhNEc8IPmmeQpf4vU/ESF01/VKpofeHLgp6UCfk8W8tzRmtn2yEsseAb+UKAzCzzcCDNOLt4ylkrLULIXOk51bTMSnpHOC7OEcefEzRqEPaIpBCqx8BH2f4yl4q6WI8lR7Bo4Y1beqFr75K0h24r80JpdtxXwhwgXxT0aikbgu+dZxmvRwPDTem/8OkK1aAD7vYxnAesJvGEBpmknMQJ4gMf8lRsng0Qx7vBvsXIdypeMhouAscpzerVLp3dXZtWPlFTOz/At4TWxniuA34BAtrz0gRCUY7xN69YXL64SWOAhtKybdVUvKws6XSsFBvc3RCuI7Z7ulUPtsRCxbDHMHBy58GrAzrjcXUbfhnb69nuKl7v3Jj5A1avhBQz3fQl81sL/AkvWVoi+gPa3ILqyfrvonOm2YW0T9Cx5fnu1hjv8VSfOY/MxUt9OG50BET/I5py06KLuMJRlh3Lwuri+gNIzMmqhSd/A4nauJz40UMjpj/nmpSduIJymb2MPCHdLmXLbiL6IwIM3/SaWnJgFvpvBN0Eb0hksPngEfauZFYGbkb339XwvmLOV3FeRmgiit6CrjGzJ7o9GleXF+Bf7KwPhOwGJ10R3iECeCjZrZVUmnWfRdZhPI1nDM5a06aevJD+Ba3LWb2YOLNJ7t9Tj398Y18v/P5DI8Yejlg+rshM9sOjY/DAP4PYa8a0vVR1MIAAAAASUVORK5CYII=";

    enum Stage { Idle, Follow, Approach, Ticket, Reply, Arrest, Return, PusherSearch }

    class Officer
    {
        public Ped Ped;
        public bool IsDriver;      // the officer who writes the ticket (driver of the car / nearest foot cop)
        public Vector3 Post;
        public int Step;
        public int StepAt;
    }

    class Voice
    {
        public byte[] Data;
        public int Ms;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    static extern uint XInputGetState14(uint index, out XINPUT_STATE state);

    [DllImport("xinput1_3.dll", EntryPoint = "XInputGetState")]
    static extern uint XInputGetState13(uint index, out XINPUT_STATE state);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

    Random rng = new Random();
    List<Officer> officers = new List<Officer>();
    Dictionary<string, List<string>> replies = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, Voice> voices = new Dictionary<string, Voice>(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, int> lastPick = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    Dictionary<string, AnimationSet> animSets = new Dictionary<string, AnimationSet>();
    Dictionary<string, bool> loggedOnce = new Dictionary<string, bool>();
    Model[] policeModels;

    string root = "";
    string logPath = "";
    string lastLog = "";
    string lastWavInfo = "";
    uint myPid = 0;

    Stage stage = Stage.Idle;
    int nextLogic;
    Vehicle copCar;                 // null when the officers were on foot
    Vehicle playerVeh;
    bool playerIsBike;
    string reason = "Speeding";
    string episodeFolder = "IV";

    // input
    int xiMode = 14;
    uint padIndex = 0;
    bool prevDeny, prevAccept, denyLatch, acceptLatch;

    // idle detection
    int speedingSince;
    int nextIdleCheck;
    int cooldownUntil;

    // speeding follow
    int nextRetask;
    bool pullOverSaid;
    bool steering;
    bool vanillaChasing;
    bool uTurning;
    int outOfViewSince;
    int farHiddenSince;            // catch-up: the squad car has been far back and off your screen since
    float farGapAtStart;
    int uActIdx;
    int uActCheckAt;
    float uActHeading;
    bool uSwapped;
    bool uTurned;
    float uQuatSign;
    float uLastHeading;
    int uFrames;
    float uTurnDir;
    int uTurnStart;
    int uTurnCooldown;
    int driveMode;
    int copStalledSince;
    int paperPreset;
    bool presetKeyDown;
    Vector3 lastDriveTarget;
    float lastDriveSpeed;
    int lastDriveAt;
    bool lineDone;
    bool lineReverse;
    int lineReverseUntil;
    int lineTries;
    bool backupHeld;
    int lastSteerAt;
    int caughtAt;
    bool playerIsTruck;
    int arrestStart;
    bool arrestForced;
    bool arrestOnFoot;
    bool carArrest;
    int placedAt;
    bool noCashReply;
    bool holdFire;
    bool doorCopAimOnly;
    int arrestFootStart;
    int arrestFootTaskAt;

    bool arrestPullSeen;
    int arrestPullAt;
    bool gesturesBlocked;
    int carPoseAt;
    bool carPoseHeld;
    float carPoseTime;
    List<string> preloadQueue = new List<string>();
    int preloadNextAt;
    string playerLoopAnim = "";
    bool playerIntroPending;
    bool playerIntroSeen;
    int playerIntroStart;
    // officer speech in progress
    Ped sayPed;
    List<string> sayPhrases = new List<string>();
    int sayAttempt;
    int sayCheckAt;
    Dictionary<int, string> forcedVoice = new Dictionary<int, string>();
    int playerStoppedSince;
    int bothStoppedSince;
    int followDeadline;
    bool stoppedOnce;
    bool parked;

    // fight approach
    int stopStartedAt;

    // Pusher (drug dealing script): a stop can turn into a trunk search / frisk
    bool searchRolled;              // asked Pusher once this stop
    bool resumeNoRadio;             // back from a search: the partner goes straight to his standing pose
    int pusherSearchSince;

    // ticket
    Vector3 stopPos;
    int ticketDeadline;
    bool accepted;
    int replyStep;
    int replyAt;
    bool peacefulReturn;
    int returnStart;
    int acceptChosenAt;
    int afterAt;                    // when the "acceptafter" line is due (0 = none pending)
    int copTakeAt;
    bool putAwayPending;
    int putAwayStart;
    int copTookAt;                  // when the officer's take animation started (0 = it has not)
    GTA.Object radioProp;
    GTA.Object paperProp;
    bool paperWanted;
    int paperRetryAt;
    int radioRetryAt;
    RadioStation savedRadio;
    bool radioSaved;
    int radioGuardUntil;
    int radioNextFix;

    // animation state
    bool playerAnimOn;
    bool secondaryBroken;
    string playerAnimSet = "";
    string playerAnimName = "";
    bool writePending;
    bool writeFrozen;
    int writeStart;

    System.Media.SoundPlayer voicePlayer;
    System.IO.MemoryStream voiceStream;

    public StopRightThere()
    {
        this.Interval = 0;
        myPid = (uint)Process.GetCurrentProcess().Id;

        string gameDir = "";
        try
        {
            gameDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName);
        }
        catch (Exception)
        {
            gameDir = Directory.GetCurrentDirectory();
        }

        root = Path.Combine(gameDir, FolderName);
        logPath = Path.Combine(gameDir, "scripts\\StopRightThere.log");
        try
        {
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 256 * 1024) File.Delete(logPath);
        }
        catch (Exception) { }

        Log("=== StopRightThere build 2026-10-05 v19e | volume " + VoiceVolume + " | folder " + root + " ===");
        if (!Directory.Exists(root)) Log("Folder does not exist: " + root);

        policeModels = new Model[PoliceCarModels.Length];
        for (int i = 0; i < PoliceCarModels.Length; i++) policeModels[i] = new Model(PoliceCarModels[i]);

        LoadConfig();
        LoadReplies();
        LoadTextTextures();
        LoadIcons();

        this.PerFrameDrawing += new GraphicsEventHandler(OnDraw);
        this.Tick += new EventHandler(OnTick);
    }

    // ----------------------------------------------------------------- log

    void Log(string msg)
    {
        if (!EnableLogging) return;
        if (msg == lastLog) return;
        lastLog = msg;
        try
        {
            File.AppendAllText(logPath, DateTime.Now.ToString("HH:mm:ss") + "  " + msg + "\r\n");
        }
        catch (Exception) { }
    }

    void LogOnce(string key, string msg)
    {
        if (loggedOnce.ContainsKey(key)) return;
        loggedOnce[key] = true;
        Log(msg);
    }

    // ----------------------------------------------------------------- replies.txt

    void LoadReplies()
    {
        // defaults, used for any key replies.txt does not define
        SetReply("PullOver", "PULL_OVER, PULL_OVER_WARNING");
        SetReply("Speeding", "PULLED_OVER_SPEEDING, PULLED_OVER_RECKLESS");
        SetReply("Accept", "THANKS, GENERIC_BYE");
        SetReply("Deny", "INTIMIDATE");

        try
        {
            string path = Path.Combine(root, "replies.txt");
            if (!File.Exists(path)) { Log("replies.txt not found, using the built-in officer lines"); return; }
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                SetReply(line.Substring(0, eq).Trim(), line.Substring(eq + 1));
            }
            Log("replies.txt loaded");
        }
        catch (Exception ex) { Log("replies.txt failed: " + ex.Message); }
    }

    void SetReply(string key, string csv)
    {
        List<string> list = new List<string>();
        foreach (string part in csv.Split(','))
        {
            string p = part.Trim();
            if (p.Length > 0) list.Add(p);
        }
        replies[key] = list;
    }

    // ----------------------------------------------------------------- traffic held at the stop
    // From the moment an officer is out of the squad car until both are back in it (or the stop ends), the
    // stop is a closed stretch of road: traffic from behind stops 7 m behind the squad car's rear bumper,
    // traffic from the front stops 5 m in front of your front bumper, the whole width of the road. Cars ease
    // in over the last 10 m and stop at the line; a stopped driver's driving order is cleared (no gas, no
    // burnout) and he waits. Police cars are never touched. When it is over they drive on.
    const float HoldBehindSquadCar = 7f, HoldAheadOfYou = 5f, HoldRoadHalfWidth = 12f;
    int holdScanAt;
    readonly List<KeyValuePair<Vehicle, float>> holdCaps = new List<KeyValuePair<Vehicle, float>>();
    readonly Dictionary<int, KeyValuePair<Vehicle, Ped>> holdParked = new Dictionary<int, KeyValuePair<Vehicle, Ped>>();

    void TickHoldTraffic(int now)
    {
        bool copOut = false;
        if (stage != Stage.Idle && stage != Stage.Follow && stage != Stage.Return && Ok(copCar) && Ok(playerVeh))
            foreach (Officer o in officers) if (Ok(o.Ped) && !o.Ped.isInVehicle()) { copOut = true; break; }
        if (!copOut) { if (holdParked.Count > 0 || holdCaps.Count > 0) ReleaseHeldTraffic(); return; }

        if (now - holdScanAt > 100 || now < holdScanAt)
        {
            holdScanAt = now;
            holdCaps.Clear();
            Vector3 cp = copCar.Position, pp = playerVeh.Position;
            float ux = pp.X - cp.X, uy = pp.Y - cp.Y, ul = (float)Math.Sqrt(ux * ux + uy * uy);
            if (ul < 1f) { Forward(playerVeh.Heading, out ux, out uy); ul = 1f; }
            else { ux /= ul; uy /= ul; }
            float rearLine = -(copCar.Model.GetDimensions().Y * 0.5f) - HoldBehindSquadCar;
            float frontLine = ul + playerVeh.Model.GetDimensions().Y * 0.5f + HoldAheadOfYou;
            Ped me = Game.LocalPlayer.Character;
            Vector3 mid = new Vector3(cp.X + ux * ul * 0.5f, cp.Y + uy * ul * 0.5f, cp.Z);
            foreach (Vehicle v in World.GetVehicles(mid, 60f))
            {
                try
                {
                    if (!Ok(v) || v == copCar || v == playerVeh || IsPoliceCar(v)) continue;
                    Ped d = v.GetPedOnSeat(VehicleSeat.Driver);
                    if (!Ok(d) || d == me) continue;
                    bool officer = false;
                    foreach (Officer o in officers) if (Ok(o.Ped) && o.Ped == d) officer = true;
                    if (officer) continue;
                    Vector3 vp = v.Position;
                    float rx = vp.X - cp.X, ry = vp.Y - cp.Y;
                    float along = rx * ux + ry * uy, side = rx * -uy + ry * ux;
                    if (Math.Abs(side) > HoldRoadHalfWidth) continue;
                    float halfLen = v.Model.GetDimensions().Y * 0.5f;
                    float fx, fy;
                    Forward(v.Heading, out fx, out fy);
                    float dir = fx * ux + fy * uy;
                    Vector3 vel = v.Velocity;
                    if (vel.X * fx + vel.Y * fy < -0.5f) dir = -dir;
                    float room;
                    if (dir > 0.3f) room = rearLine - (along + halfLen);
                    else if (dir < -0.3f) room = (along - halfLen) - frontLine;
                    else room = (along > rearLine - halfLen && along < frontLine + halfLen) ? 0f : 99f;
                    if (dir > 0.3f && along - halfLen > frontLine) continue;
                    if (dir < -0.3f && along + halfLen < rearLine) continue;
                    if (room > 45f) continue;
                    // a steady, even braking (2.5 m/s each second) that brings it to rest exactly at the line
                    holdCaps.Add(new KeyValuePair<Vehicle, float>(v, room <= 0.05f ? 0f : (float)Math.Sqrt(2.0 * 2.5 * room)));
                }
                catch (Exception) { }
            }
        }
        foreach (KeyValuePair<Vehicle, float> hv in holdCaps)
        {
            try
            {
                Vehicle v = hv.Key;
                if (!Ok(v)) continue;
                if (v.Speed > hv.Value) v.Speed = hv.Value;
                if (hv.Value <= 0f && v.Speed < 0.6f)
                {
                    int key = v.GetHashCode();
                    if (!holdParked.ContainsKey(key))
                    {
                        Ped d = v.GetPedOnSeat(VehicleSeat.Driver);
                        if (Ok(d)) { try { d.Task.ClearAll(); } catch (Exception) { } holdParked[key] = new KeyValuePair<Vehicle, Ped>(v, d); }
                    }
                    v.Speed = 0f;
                }
            }
            catch (Exception) { }
        }
        foreach (KeyValuePair<Vehicle, Ped> kv in holdParked.Values)
        {
            try { if (Ok(kv.Key)) kv.Key.Speed = 0f; } catch (Exception) { }
        }
    }

    void ReleaseHeldTraffic()
    {
        foreach (KeyValuePair<Vehicle, Ped> kv in holdParked.Values)
        {
            try { if (Ok(kv.Key) && Ok(kv.Value) && kv.Value.isInVehicle(kv.Key)) kv.Value.Task.CruiseWithVehicle(kv.Key, 10f, true); }
            catch (Exception) { }
        }
        holdParked.Clear();
        holdCaps.Clear();
    }

    // ----------------------------------------------------------------- helpers

    static bool Ok(Ped p)
    {
        try { return !object.ReferenceEquals(p, null) && p.Exists(); }
        catch (Exception) { return false; }
    }

    static bool Ok(Vehicle v)
    {
        try { return !object.ReferenceEquals(v, null) && v.Exists(); }
        catch (Exception) { return false; }
    }

    static float Dist2D(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy);
    }

    static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }

    static void Forward(float headingDeg, out float fx, out float fy)
    {
        double r = headingDeg * Math.PI / 180.0;
        fx = (float)-Math.Sin(r);
        fy = (float)Math.Cos(r);
    }

    void TryNative(string name, params Parameter[] args)
    {
        try { Function.Call(name, args); }
        catch (Exception ex) { LogOnce(name, name + " failed: " + ex.Message); }
    }

    bool IsPoliceCar(Vehicle v)
    {
        int h = v.Model.Hash;
        for (int i = 0; i < policeModels.Length; i++) if (policeModels[i].Hash == h) return true;
        return false;
    }

    bool ModelIn(Vehicle v, string[] names)
    {
        int h = v.Model.Hash;
        foreach (string n in names)
        {
            if (new Model(n).Hash == h) return true;
        }
        return false;
    }

    string BikePayAnim(Vehicle v)
    {
        if (ModelIn(v, ScooterBikes)) return BikePayScooter;
        if (ModelIn(v, DirtBikes)) return BikePayDirt;
        if (ModelIn(v, FreewayBikes)) return BikePayFreeway;
        if (ModelIn(v, ChopperBikes)) return BikePayChopper;
        if (ModelIn(v, SportBikes)) return BikePaySport;
        return episodeFolder == "TLAD" ? BikePayChopper : BikePaySport;
    }

    // scripts\\StopRightThere\\config.ini - created with the default the first time the game runs
    void LoadConfig()
    {
        try
        {
            string path = Path.Combine(root, "config.ini");
            if (!File.Exists(path))
            {
                if (Directory.Exists(root))
                {
                    File.WriteAllText(path,
                        "; StopRightThere settings. Change the number, save, and restart the game.\r\n" +
                        ";\r\n" +
                        "; SpeedLimit = how fast (in MPH) you have to be going near a squad car to get pulled over.\r\n" +
                        "\r\n" +
                        "SpeedLimit = 45\r\n" +
                        "\r\n" + ChanceSettingText);
                    Log("config.ini created with the defaults (45 mph, 50% stop chance)");
                }
                return;
            }
            bool sawChance = false;
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#") || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLower();
                if (key == "stopchance") sawChance = true;
                string val = line.Substring(eq + 1).Trim();
                int cut = val.IndexOf(';');
                if (cut >= 0) val = val.Substring(0, cut).Trim();
                if (key == "stopchance")
                {
                    float c;
                    if (float.TryParse(val.Replace("%", "").Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out c) && c >= 0f && c <= 100f)
                        StopChancePct = (int)Math.Round(c);
                    else Log("config.ini: StopChance '" + val + "' is not a number between 0 and 100, using " + StopChancePct + "%");
                }
                if (key == "speedlimit")
                {
                    float v;
                    if (float.TryParse(val.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && v >= 5f && v <= 300f)
                        SpeedLimitMph = v;
                    else Log("config.ini: SpeedLimit '" + val + "' is not a number between 5 and 300, using " + SpeedLimitMph + " mph");
                }
            }
            if (!sawChance) AppendChanceSetting(path);
            Log("config.ini loaded: speed limit " + SpeedLimitMph + " mph, stop chance " + StopChancePct + "%");
        }
        catch (Exception ex) { Log("config.ini could not be read (" + ex.Message + "), using " + SpeedLimitMph + " mph"); }
    }

    // any law-enforcement vehicle, not just the four squad cars: nobody pulls over one of their own
    static readonly string[] LawVehicleModels = { "POLICE", "POLICE2", "POLICE3", "POLICE4", "POLICEB", "POLICEW", "NOOSE", "NSTOCKADE", "PSTOCKADE", "FBI", "POLPATRIOT", "POLMAV", "PREDATOR", "SWAT" };
    Model[] lawModels;
    bool PlayerInPoliceVehicle(Ped pp, Vehicle pv)
    {
        try { if (Function.Call<bool>("IS_CHAR_IN_ANY_POLICE_VEHICLE", pp)) return true; }
        catch (Exception) { }
        try
        {
            if (lawModels == null)
            {
                lawModels = new Model[LawVehicleModels.Length];
                for (int i = 0; i < LawVehicleModels.Length; i++) lawModels[i] = new Model(LawVehicleModels[i]);
            }
            int h = pv.Model.Hash;
            foreach (Model lm in lawModels) if (lm.Hash == h) return true;
        }
        catch (Exception) { }
        return false;
    }

    const string ChanceSettingText =
        "; StopChance = the chance (0 to 100 %) that a squad car which catches you speeding actually pulls you over.\r\n" +
        ";   Each squad car decides once; one that lets you go ignores you for the next minute.\r\n" +
        ";   0 = never, 100 = every time.\r\n" +
        "StopChance = 50\r\n";

    // an older config.ini without the StopChance line gets it added (the user's other settings are kept)
    void AppendChanceSetting(string path)
    {
        try
        {
            File.AppendAllText(path, "\r\n" + ChanceSettingText);
            Log("config.ini: StopChance setting added with the default 50%");
        }
        catch (Exception) { }
    }

    // squad cars that already decided whether to pull you over (each one decides once per minute)
    class CopDecision { public Vehicle Car; public bool Stops; public int Until; }
    List<CopDecision> copDecisions = new List<CopDecision>();

    CopDecision FindDecision(Vehicle v, int now)
    {
        for (int i = copDecisions.Count - 1; i >= 0; i--)
        {
            CopDecision d = copDecisions[i];
            if (now > d.Until || !Ok(d.Car)) { copDecisions.RemoveAt(i); continue; }
            if (d.Car == v) return d;
        }
        return null;
    }

    bool MissionActive()
    {
        try { return Function.Call<bool>("GET_MISSION_FLAG"); }
        catch (Exception) { return false; }
    }

    void PoliceIgnore(bool on)
    {
        try
        {
            int pid = Function.Call<int>("GET_PLAYER_ID");
            Function.Call("SET_POLICE_IGNORE_PLAYER", pid, on);
        }
        catch (Exception ex) { LogOnce("ignore", "SET_POLICE_IGNORE_PLAYER failed: " + ex.Message); }
    }

    void WalkTo(Ped p, Vector3 pos, bool run)
    {
        TryNative("TASK_GO_STRAIGHT_TO_COORD", p, pos.X, pos.Y, pos.Z, run ? 3 : 2, 20000);
    }

    float DesiredCenterGap()
    {
        float mine = playerIsBike ? 1.1f : 2.4f;
        float his = 2.5f;
        try
        {
            if (Ok(playerVeh)) mine = playerVeh.Model.GetDimensions().Y * 0.5f;
            if (Ok(copCar)) his = copCar.Model.GetDimensions().Y * 0.5f;
        }
        catch (Exception) { }
        return mine + his + ParkGapMeters;
    }

    string DetectEpisodeFolder()
    {
        try
        {
            GameEpisode ep = Game.CurrentEpisode;
            if (ep == GameEpisode.TLAD) return "TLAD";
            if (ep == GameEpisode.TBOGT) return "TBoGT";
        }
        catch (Exception ex) { Log("Episode detection failed (" + ex.Message + "), assuming IV"); }
        return "IV";
    }

    void TakeOfficer(Ped p, bool primary, bool inCar)
    {
        Officer o = new Officer();
        o.Ped = p;
        o.IsDriver = primary;
        o.Step = inCar ? 0 : 1;
        o.StepAt = 0;
        p.isRequiredForMission = true;
        p.BlockPermanentEvents = true;
        p.Task.AlwaysKeepTask = true;
        officers.Add(o);
    }

    // ----------------------------------------------------------------- animations

    AnimationSet Set(string name)
    {
        AnimationSet s;
        if (!animSets.TryGetValue(name, out s))
        {
            s = new AnimationSet(name);
            animSets[name] = s;
        }
        return s;
    }

    void Preload(string name)
    {
        try { Function.Call("REQUEST_ANIMS", name); }
        catch (Exception) { }
    }

    // asks the game for an animation set and waits a moment for it if it is not in memory yet
    bool EnsureAnims(string name)
    {
        try
        {
            Function.Call("REQUEST_ANIMS", name);
            for (int i = 0; i < 60; i++)
            {
                if (Function.Call<bool>("HAVE_ANIMS_LOADED", name)) return true;
                Wait(0);
                Function.Call("REQUEST_ANIMS", name);
            }
        }
        catch (Exception ex) { LogOnce("req" + name, "REQUEST_ANIMS " + name + " failed: " + ex.Message); }
        return false;
    }

    void Anim(Ped p, string set, string name, AnimationFlags flags)
    {
        try { p.Task.PlayAnimation(Set(set), name, 8.0f, flags); }
        catch (Exception ex) { LogOnce("anim" + set + name, "Animation " + set + "/" + name + " failed: " + ex.Message); }
    }

    float AnimTime(Ped p, string set, string name)
    {
        try { return p.Animation.GetCurrentAnimationTime(Set(set), name); }
        catch (Exception) { return 0f; }
    }

    bool AnimPlaying(Ped p, string set, string name)
    {
        try { return p.Animation.isPlaying(Set(set), name); }
        catch (Exception) { return true; }
    }

    void AnimSpeed(Ped p, string set, string name, float speed)
    {
        TryNative("SET_CHAR_ANIM_SPEED", p, set, name, speed);
    }

    // an animation on the player while he sits in / on his vehicle
    void PlayerVehAnim(string set, string name, bool loop)
    {
        Ped pp = Game.LocalPlayer.Character;
        ReleasePlayerHold();
        playerAnimSet = set;
        playerAnimName = name;
        playerAnimOn = true;
        try
        {
            AnimationSet s = Set(set);
            if (!EnsureAnims(set))
            {
                LogOnce("load" + set, "Animation set '" + set + "' did not load, skipping " + name);
                return;
            }
            if (PlayerAnimsAsSecondary && !secondaryBroken)
            {
                try
                {
                    Function.Call("TASK_PLAY_ANIM_SECONDARY_IN_CAR", pp, name, set, 8.0f, loop ? 1 : 0, 0, 0, 0, -1);
                    Log("Player animation (secondary): " + set + "/" + name);
                    return;
                }
                catch (Exception ex)
                {
                    secondaryBroken = true;
                    Log("TASK_PLAY_ANIM_SECONDARY_IN_CAR failed (" + ex.Message + "), using the normal animation task");
                }
            }
            pp.Task.PlayAnimation(s, name, 8.0f, loop ? LoopFlag : AnimationFlags.None);
            Log("Player animation: " + set + "/" + name);
        }
        catch (Exception ex) { Log("Player animation " + set + "/" + name + " failed: " + ex.Message); }
    }

    void ReleasePlayerHold()
    {
        playerIntroPending = false;
        if (playerAnimOn && playerAnimName.Length > 0)
            AnimSpeed(Game.LocalPlayer.Character, playerAnimSet, playerAnimName, 1.0f);
    }

    void StopPlayerAnim()
    {
        if (!playerAnimOn) return;
        ReleasePlayerHold();
        playerAnimOn = false;
        Ped pp = Game.LocalPlayer.Character;
        try { pp.Task.ClearSecondary(); }
        catch (Exception) { }
        if (!PlayerAnimsAsSecondary || secondaryBroken)
        {
            try { pp.Task.ClearAll(); }
            catch (Exception) { }
        }
    }

    // license intro once, then straight into the license loop
    void TickPlayerIntro(int now)
    {
        if (!playerIntroPending) return;
        Ped pp = Game.LocalPlayer.Character;
        bool playing = AnimPlaying(pp, playerAnimSet, playerAnimName);
        if (playing) playerIntroSeen = true;
        float t = AnimTime(pp, playerAnimSet, playerAnimName);
        int el = now - playerIntroStart;
        if (t >= 0.94f || (playerIntroSeen && !playing) || el > 6000 || (!playerIntroSeen && el > 1500))
        {
            PlayerVehAnim(PlayerIntroSet, playerLoopAnim, true);
        }
    }

    // officer pulls the ticket out and is then held with it in his hand
    void StartWriting(Ped cop, int now)
    {
        // motorcycle / truck / van: he stands at your door looking around (no ticket in his hand, nothing held
        // or frozen). It is simply replaced by his next animation the moment you answer.
        Anim(cop, WriteSet, WriteAnim, LoopFlag);
        writePending = false;
        writeFrozen = false;
        writeStart = now;
    }

    void GivePaper(Ped p)
    {
        if (PaperModelName.Length == 0) return;
        if (!object.ReferenceEquals(paperProp, null)) return;
        if (Game.GameTime < paperRetryAt) return;
        paperRetryAt = Game.GameTime + 300;
        try
        {
            Model m = new Model(PaperModelName);
            if (!m.isValid) { LogOnce("papermodel", "Ticket prop: the game does not know a model called '" + PaperModelName + "'"); return; }
            Function.Call("REQUEST_MODEL", m);
            if (!m.isInMemory) { LogOnce("paperwait", "Ticket prop: model requested, waiting for it to load"); return; }
            paperProp = World.CreateObject(m, p.Position);
            if (object.ReferenceEquals(paperProp, null)) { LogOnce("papercreate", "Ticket prop: the game refused to create '" + PaperModelName + "'"); return; }
            paperProp.AttachToPed(p, PaperInLeftHand ? Bone.LeftHand : Bone.RightHand, new Vector3(PaperOffsetX, PaperOffsetY, PaperOffsetZ), new Vector3(PaperRotX, PaperRotY, PaperRotZ));
            Log("Ticket prop attached");
        }
        catch (Exception ex) { Log("Ticket prop failed: " + ex.Message); }
    }

    int tuneKeysDown;

    // in-game adjustment of the ticket prop (see PropTuning at the top of the file)
    void TickPropTuning()
    {
        if (!PropTuning || object.ReferenceEquals(paperProp, null) || officers.Count == 0 || !Ok(officers[0].Ped)) return;
        int keys = 0;
        int[] vk = { 0x64, 0x66, 0x68, 0x62, 0x67, 0x69 };   // numpad 4 6 8 2 7 9
        for (int i = 0; i < vk.Length; i++)
        {
            if ((GetAsyncKeyState(vk[i]) & 0x8000) != 0) keys |= 1 << i;
        }
        int pressed = keys & ~tuneKeysDown;
        tuneKeysDown = keys;

        // P = next way round, O = previous: steps the ticket through all 24 ways it can sit in his hand
        bool pKey = (GetAsyncKeyState(0x50) & 0x8000) != 0;
        bool oKey = (GetAsyncKeyState(0x4F) & 0x8000) != 0;
        bool presetHit = (pKey || oKey) && !presetKeyDown;
        presetKeyDown = pKey || oKey;
        if (presetHit)
        {
            paperPreset = (paperPreset + (pKey ? 1 : 23)) % 24;
            if (paperPreset < 16) { PaperRotX = (paperPreset % 4) * 90f; PaperRotY = 0f; PaperRotZ = (paperPreset / 4) * 90f; }
            else { PaperRotX = 0f; PaperRotY = paperPreset < 20 ? 90f : 270f; PaperRotZ = (paperPreset % 4) * 90f; }
        }
        if (pressed == 0 && !presetHit) return;

        bool turn = (GetAsyncKeyState(0x60) & 0x8000) != 0;   // numpad 0 held
        float d = turn ? 5f : 0.01f;
        if (turn)
        {
            if ((pressed & 1) != 0) PaperRotZ -= d;
            if ((pressed & 2) != 0) PaperRotZ += d;
            if ((pressed & 4) != 0) PaperRotX += d;
            if ((pressed & 8) != 0) PaperRotX -= d;
            if ((pressed & 16) != 0) PaperRotY -= d;
            if ((pressed & 32) != 0) PaperRotY += d;
        }
        else
        {
            if ((pressed & 1) != 0) PaperOffsetX -= d;
            if ((pressed & 2) != 0) PaperOffsetX += d;
            if ((pressed & 4) != 0) PaperOffsetY += d;
            if ((pressed & 8) != 0) PaperOffsetY -= d;
            if ((pressed & 16) != 0) PaperOffsetZ -= d;
            if ((pressed & 32) != 0) PaperOffsetZ += d;
        }

        string txt = (presetHit ? "TICKET POSITION " + (paperPreset + 1) + " of 24   " : "") + "Ticket prop  offset " + PaperOffsetX.ToString("0.00") + ", " + PaperOffsetY.ToString("0.00") + ", " + PaperOffsetZ.ToString("0.00")
            + "   rotation " + PaperRotX.ToString("0") + ", " + PaperRotY.ToString("0") + ", " + PaperRotZ.ToString("0");
        Log(txt);
        try { Game.DisplayText(txt, 4000); }
        catch (Exception) { }

        // put it back in his hand at the new position
        try
        {
            paperProp.Detach();
            paperProp.AttachToPed(officers[0].Ped, PaperInLeftHand ? Bone.LeftHand : Bone.RightHand, new Vector3(PaperOffsetX, PaperOffsetY, PaperOffsetZ), new Vector3(PaperRotX, PaperRotY, PaperRotZ));
        }
        catch (Exception ex) { Log("Ticket prop re-attach failed: " + ex.Message); }
    }

    void RemovePaper()
    {
        paperWanted = false;
        try
        {
            if (!object.ReferenceEquals(paperProp, null)) paperProp.Delete();
        }
        catch (Exception) { }
        paperProp = null;
    }

    void TickWriting(int now)
    {
        if (carPoseAt != 0 && now >= carPoseAt && officers.Count > 0 && Ok(officers[0].Ped))
        {
            carPoseAt = 0;
            carPoseHeld = true;
            carPoseTime = AnimTime(officers[0].Ped, CopCarAnimSet, CopCarAnim);
            AnimSpeed(officers[0].Ped, CopCarAnimSet, CopCarAnim, 0.0f);
        }
        else if (carPoseHeld && officers.Count > 0 && Ok(officers[0].Ped))
        {
            // hand on the car and nothing else: every frame the pose is pinned again, so talking,
            // gestures or the game restarting the animation cannot move his arms
            Ped hc = officers[0].Ped;
            TryNative("BLOCK_CHAR_GESTURE_ANIMS", hc, true);
            if (!AnimPlaying(hc, CopCarAnimSet, CopCarAnim)) Anim(hc, CopCarAnimSet, CopCarAnim, LoopFlag);
            if (carPoseTime > 0.0001f) TryNative("SET_CHAR_ANIM_CURRENT_TIME", hc, CopCarAnimSet, CopCarAnim, carPoseTime);
            AnimSpeed(hc, CopCarAnimSet, CopCarAnim, 0.0f);
        }
        if (paperWanted && officers.Count > 0 && Ok(officers[0].Ped)) GivePaper(officers[0].Ped);   // keeps trying until the prop has loaded
        TickPropTuning();
        if (!writePending || officers.Count == 0) return;
        Ped cop = officers[0].Ped;
        float t = AnimTime(cop, WriteSet, WriteAnim);
        bool byTime = t >= WriteHoldAt;
        bool byClock = t <= 0.0001f && now - writeStart >= WriteHoldFallbackMs;
        if (byTime || byClock)
        {
            AnimSpeed(cop, WriteSet, WriteAnim, 0.0f);
            writePending = false;
            writeFrozen = true;
        }
    }

    // putAway = let the animation run on so he pockets the ticket
    void StopWriting(bool putAway)
    {
        // let go of the held hand-on-car pose too
        carPoseAt = 0;
        if (carPoseHeld && officers.Count > 0 && Ok(officers[0].Ped))
            AnimSpeed(officers[0].Ped, CopCarAnimSet, CopCarAnim, 1.0f);
        carPoseHeld = false;
        RemovePaper();
        writePending = false;
        // (putAway no longer matters: a held animation is always let go, otherwise he stays stuck in the pose
        //  and cannot carry out whatever he is told to do next)
        if (writeFrozen && officers.Count > 0 && Ok(officers[0].Ped))
            AnimSpeed(officers[0].Ped, WriteSet, WriteAnim, 1.0f);
        writeFrozen = false;
    }

    void GiveRadio(Ped p)
    {
        if (RadioModelName.Length == 0) return;
        if (!object.ReferenceEquals(radioProp, null)) return;
        if (Game.GameTime < radioRetryAt) return;
        radioRetryAt = Game.GameTime + 300;
        try
        {
            Model m = new Model(RadioModelName);
            if (!m.isValid) { LogOnce("radiomodel", "Radio prop: the game does not know a model called '" + RadioModelName + "'"); return; }
            Function.Call("REQUEST_MODEL", m);
            if (!m.isInMemory) { LogOnce("radiowait", "Radio prop: model requested, waiting for it to load"); return; }
            radioProp = World.CreateObject(m, p.Position);
            if (object.ReferenceEquals(radioProp, null)) { LogOnce("radiocreate", "Radio prop: the game refused to create '" + RadioModelName + "'"); return; }
            radioProp.AttachToPed(p, Bone.RightHand, new Vector3(RadioOffsetX, RadioOffsetY, RadioOffsetZ), new Vector3(RadioRotX, RadioRotY, RadioRotZ));
            Log("Radio prop attached");
        }
        catch (Exception ex) { Log("Radio prop failed: " + ex.Message); }
    }

    void RemoveRadio()
    {
        try
        {
            if (!object.ReferenceEquals(radioProp, null)) radioProp.Delete();
        }
        catch (Exception) { }
        radioProp = null;
    }

    // officer reached his spot and has turned to face you
    void Arrive(Officer o, Ped pp, int now)
    {
        if (!o.IsDriver && resumeNoRadio)
        {
            // back from a Pusher search: arms crossed, no radio
            TryNative("TASK_LOOK_AT_CHAR", o.Ped, pp, 600000, 0);
            Anim(o.Ped, PartnerIdleSet, PartnerIdleAnim, LoopFlag);
            o.Step = 4;
            o.StepAt = now;
            return;
        }
        if (!o.IsDriver)
        {
            TryNative("TASK_LOOK_AT_CHAR", o.Ped, pp, 600000, 0);
            Anim(o.Ped, PartnerRadioSet, PartnerRadioAnim, AnimationFlags.None);
            GiveRadio(o.Ped);
        }
        o.Step = 3;
        o.StepAt = now;
    }

    // second officer: still walking up, or done with the radio and into his standing idle
    void TickPartner(int now)
    {
        if (officers.Count < 2) return;
        Officer o = officers[1];
        if (o.Step < 3)
        {
            CarOfficerStep(o, now);
        }
        else if (o.Step == 3)
        {
            int el = now - o.StepAt;
            bool playing = el < 700 || AnimPlaying(o.Ped, PartnerRadioSet, PartnerRadioAnim);
            if (playing) GiveRadio(o.Ped);   // keeps trying until the prop has loaded
            if (!playing || el > PartnerRadioMaxMs)
            {
                RemoveRadio();
                Anim(o.Ped, PartnerIdleSet, PartnerIdleAnim, LoopFlag);
                o.Step = 4;
            }
        }
    }

    // ----------------------------------------------------------------- audio (same method as StrangerSystem)

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
            string id = Encoding.ASCII.GetString(src, pos, 4);
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
            if (isFloat)
            {
                v = (bits == 32) ? BitConverter.ToSingle(src, p) : BitConverter.ToDouble(src, p);
            }
            else if (bits == 8)
            {
                v = (src[p] - 128) / 128.0;
            }
            else if (bits == 16)
            {
                v = BitConverter.ToInt16(src, p) / 32768.0;
            }
            else if (bits == 24)
            {
                int t = src[p] | (src[p + 1] << 8) | (src[p + 2] << 16);
                if ((t & 0x800000) != 0) t -= 0x1000000;
                v = t / 8388608.0;
            }
            else
            {
                v = BitConverter.ToInt32(src, p) / 2147483648.0;
            }

            int s16 = (int)Math.Round(v * gain * 32767.0);
            if (s16 > 32767) s16 = 32767;
            if (s16 < -32768) s16 = -32768;
            outData[n * 2] = (byte)(s16 & 0xFF);
            outData[n * 2 + 1] = (byte)((s16 >> 8) & 0xFF);
        }

        byte[] wav = new byte[44 + outData.Length];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
        BitConverter.GetBytes(36 + outData.Length).CopyTo(wav, 4);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
        BitConverter.GetBytes(16).CopyTo(wav, 16);
        BitConverter.GetBytes((short)1).CopyTo(wav, 20);
        BitConverter.GetBytes((short)channels).CopyTo(wav, 22);
        BitConverter.GetBytes(rate).CopyTo(wav, 24);
        BitConverter.GetBytes(rate * channels * 2).CopyTo(wav, 28);
        BitConverter.GetBytes((short)(channels * 2)).CopyTo(wav, 32);
        BitConverter.GetBytes((short)16).CopyTo(wav, 34);
        Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
        BitConverter.GetBytes(outData.Length).CopyTo(wav, 40);
        outData.CopyTo(wav, 44);
        return wav;
    }

    Voice LoadVoice(string file)
    {
        Voice v;
        if (voices.TryGetValue(file, out v)) return v;
        v = null;
        try
        {
            byte[] raw = File.ReadAllBytes(file);
            byte[] converted = ToPcm16Wav(raw, VoiceVolume / 1000.0);
            if (converted == null)
            {
                Log("NOT A STANDARD WAV: " + file + "  (" + lastWavInfo + ") - re-export it as a normal PCM .wav");
            }
            else
            {
                int ch = BitConverter.ToInt16(converted, 22);
                int rate = BitConverter.ToInt32(converted, 24);
                v = new Voice();
                v.Data = converted;
                v.Ms = (int)((long)(converted.Length - 44) * 1000L / (long)(rate * ch * 2));
                Log("Voice file ready: " + Path.GetFileName(file) + "  [" + lastWavInfo + "]");
            }
        }
        catch (Exception ex) { Log("Voice file failed: " + file + ": " + ex.Message); }
        voices[file] = v;
        return v;
    }

    // plays a wav straight from memory, returns its length in ms (0 if it could not play)
    int PlayVoice(string file)
    {
        Voice v = LoadVoice(file);
        if (v == null) return 0;
        try
        {
            if (voicePlayer != null) { voicePlayer.Stop(); voicePlayer.Dispose(); voicePlayer = null; }
            if (voiceStream != null) { voiceStream.Dispose(); voiceStream = null; }
            voiceStream = new System.IO.MemoryStream(v.Data);
            voicePlayer = new System.Media.SoundPlayer(voiceStream);
            voicePlayer.Play();
        }
        catch (Exception ex)
        {
            Log("Playback failed: " + ex.Message);
            return 0;
        }
        int len = v.Ms;
        if (len < 300) len = 300;
        if (len > 8000) len = 8000;
        Log("Playing " + Path.GetFileName(file) + " (" + len + " ms)");
        return len;
    }

    int PickIndex(string key, int count)
    {
        if (count <= 1) return 0;
        int pick = rng.Next(count);
        if (count >= 3)
        {
            int last;
            if (!lastPick.TryGetValue(key, out last)) last = -1;
            if (pick == last) pick = (pick + 1) % count;
        }
        lastPick[key] = pick;
        return pick;
    }

    // random wav from scripts\StopRightThere\<IV|TLAD|TBoGT>\<folder>
    int PlayPlayerLine(string folder)
    {
        try
        {
            string dir = Path.Combine(Path.Combine(root, episodeFolder), folder);
            if (!Directory.Exists(dir)) { Log("Voice folder missing: " + dir); return 0; }
            string[] files = Directory.GetFiles(dir, "*.wav");
            if (files.Length == 0) { Log("No .wav files in " + dir); return 0; }
            Array.Sort(files);

            // one favoured line: in the chosen game + folder, this file plays most of the time
            if (string.Equals(episodeFolder, FavouredEpisode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(folder, FavouredFolder, StringComparison.OrdinalIgnoreCase) && files.Length > 1)
            {
                string fav = null;
                List<string> rest = new List<string>();
                foreach (string f in files)
                {
                    if (string.Equals(Path.GetFileName(f), FavouredFile, StringComparison.OrdinalIgnoreCase)) fav = f;
                    else rest.Add(f);
                }
                if (fav == null) LogOnce("fav", "Favoured line " + FavouredFile + " not found in " + dir);
                else if (rng.Next(100) < FavouredPct) return PlayVoice(fav);
                else return PlayVoice(rest[rng.Next(rest.Count)]);
            }

            return PlayVoice(files[PickIndex(dir, files.Length)]);
        }
        catch (Exception ex) { Log("PlayPlayerLine error: " + ex.Message); return 0; }
    }

    // officer line from replies.txt: a vanilla speech name (said in that cop's own voice)
    // or a .wav path inside the StopRightThere folder
    int OfficerSay(Ped cop, string key)
    {
        try
        {
            List<string> list;
            if (!replies.TryGetValue(key, out list) || list.Count == 0) return 0;
            string first = list[PickIndex("reply:" + key, list.Count)];
            if (first.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                return PlayVoice(Path.Combine(root, first));
            if (!Ok(cop)) return 0;

            // the picked phrase first, then the other phrases of that line as back-ups
            sayPhrases = new List<string>();
            sayPhrases.Add(first);
            foreach (string e in list)
            {
                if (e != first && !e.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) sayPhrases.Add(e);
            }
            sayPed = cop;
            sayAttempt = 0;
            SayStep(Game.GameTime);
            return SpeechGuessMs;
        }
        catch (Exception ex) { Log("Officer speech failed (" + key + "): " + ex.Message); return 0; }
    }

    string ForcedVoiceFor(Ped cop)
    {
        int h = cop.GetHashCode();
        string v;
        if (forcedVoice.TryGetValue(h, out v)) return v;
        string[] pool = cop.Model.Hash == new Model(TrafficCopModel).Hash ? TrafficCopVoices : CopVoices;
        v = pool[rng.Next(pool.Length)];
        forcedVoice[h] = v;
        return v;
    }

    // one attempt: every phrase in the cop's own voice first, then every phrase with a forced voice
    void SayStep(int now)
    {
        int n = sayPhrases.Count;
        if (!Ok(sayPed) || n == 0 || sayAttempt >= n * 2) { sayPed = null; return; }
        string phrase = sayPhrases[sayAttempt % n];
        try
        {
            if (sayAttempt < n)
            {
                Function.Call("SAY_AMBIENT_SPEECH", sayPed, phrase, 1, 1, 0);
                Log("Officer speech requested: " + phrase + " (own voice)");
            }
            else
            {
                string voice = ForcedVoiceFor(sayPed);
                Function.Call("SAY_AMBIENT_SPEECH_WITH_VOICE", sayPed, phrase, voice, 1, 1, 0);
                Log("Officer speech requested: " + phrase + " (forced voice " + voice + ")");
            }
        }
        catch (Exception ex) { Log("Officer speech error (" + phrase + "): " + ex.Message); }
        sayAttempt++;
        sayCheckAt = now + 700;
    }

    void TickSpeech(int now)
    {
        if (object.ReferenceEquals(sayPed, null) || now < sayCheckAt) return;
        if (!Ok(sayPed)) { sayPed = null; return; }
        bool speaking;
        try { speaking = Function.Call<bool>("IS_AMBIENT_SPEECH_PLAYING", sayPed); }
        catch (Exception ex) { Log("IS_AMBIENT_SPEECH_PLAYING unavailable: " + ex.Message); sayPed = null; return; }
        if (speaking)
        {
            Log("Officer IS speaking (attempt " + sayAttempt + ")");
            sayPed = null;
            return;
        }
        if (sayAttempt >= sayPhrases.Count * 2)
        {
            Log("Officer stayed silent after " + sayAttempt + " attempts");
            sayPed = null;
            return;
        }
        SayStep(now);
    }

    // ----------------------------------------------------------------- input (same method as StrangerSystem)

    bool GameHasFocus()
    {
        try
        {
            uint pid;
            GetWindowThreadProcessId(GetForegroundWindow(), out pid);
            return pid == myPid;
        }
        catch (Exception) { return true; }
    }

    uint XGet(uint idx, out XINPUT_STATE s)
    {
        s = new XINPUT_STATE();
        if (xiMode == 14)
        {
            try { return XInputGetState14(idx, out s); }
            catch (Exception) { xiMode = 13; }
        }
        if (xiMode == 13)
        {
            try { return XInputGetState13(idx, out s); }
            catch (Exception) { xiMode = -1; }
        }
        return 1167;
    }

    void UpdateInput()
    {
        bool deny = false, accept = false;
        if (GameHasFocus())
        {
            if (xiMode != -1)
            {
                XINPUT_STATE s;
                bool got = false;
                if (XGet(padIndex, out s) == 0) got = true;
                else
                {
                    for (uint i = 0; i < 4; i++)
                    {
                        if (XGet(i, out s) == 0) { padIndex = i; got = true; break; }
                    }
                }
                if (got)
                {
                    deny = (s.Gamepad.wButtons & 0x0100) != 0;     // LB
                    accept = (s.Gamepad.wButtons & 0x0200) != 0;   // RB
                }
            }
            if (EnableKeyboardFallback)
            {
                if ((GetAsyncKeyState(VK_DENY) & 0x8000) != 0) deny = true;
                if ((GetAsyncKeyState(VK_ACCEPT) & 0x8000) != 0) accept = true;
            }
        }
        if (deny && !prevDeny) denyLatch = true;
        if (accept && !prevAccept) acceptLatch = true;
        prevDeny = deny;
        prevAccept = accept;
    }

    // ----------------------------------------------------------------- main loop

    void OnTick(object sender, EventArgs e)
    {
        pending = new List<IconCmd>();
        try
        {
            UpdateInput();
            if (stage == Stage.Ticket)
            {
                UpdateResolution();
                DrawHud();
            }

            int now = Game.GameTime;
            if (uTurning && stage == Stage.Follow) UTurnFrame(now);
            else if (steering && stage == Stage.Follow) SteerFrame(now);
            else lastSteerAt = now;
            if (now >= nextLogic || now < nextLogic - 1000)
            {
                nextLogic = now + 50;
                Logic(now);
            }
        }
        catch (Exception ex) { Log("Tick error: " + ex.ToString()); }
        iconCmds = pending;
    }

    void Logic(int now)
    {
        // your follow-up remark a few seconds after accepting a ticket
        if (afterAt != 0 && now >= afterAt)
        {
            afterAt = 0;
            PlayPlayerLine("acceptafter");
        }

        GuardRadio(now);
        TickSpeech(now);
        if (stage != Stage.Idle) TickPreload(now);

        // your reaction a moment after the lights come on behind you
        if (caughtAt != 0 && now >= caughtAt)
        {
            caughtAt = 0;
            if (stage == Stage.Follow) PlayPlayerLine("Caught");
        }

        if (stage == Stage.Idle) { if (holdParked.Count > 0 || holdCaps.Count > 0) ReleaseHeldTraffic(); TickIdle(now); return; }
        // a mission started while a stop was running: the script drops everything on the spot and stays
        // out of the way (no stop can start during a mission either - see TickIdle)
        if (MissionActive())
        {
            Log("Mission started: traffic stop cancelled");
            afterAt = 0;
            caughtAt = 0;
            PoliceIgnore(false);
            Release();
            return;
        }
        // traffic is held at both ends of the stop from the moment an officer steps out of the squad car
        try { TickHoldTraffic(now); } catch (Exception ex) { LogOnce("holdtraffic", "Holding traffic failed: " + ex.Message); }
        // Pusher is running a search: it has the officers and the player, nothing else here applies
        if (stage == Stage.PusherSearch) { TickBackup(now); TickPusherSearch(now); return; }
        if (!CommonChecks(now)) return;
        TickBackup(now);

        switch (stage)
        {
            case Stage.Follow:
                {
                    string crime = FollowCrime(now);
                    if (crime != null) { Handover(crime); break; }
                    TickFollow(now);
                    break;
                }
            case Stage.Approach: TickApproach(now); break;
            case Stage.Ticket: TickTicket(now); break;
            case Stage.Reply: TickReply(now); break;
            case Stage.Arrest: TickArrest(now); break;
            case Stage.Return: TickReturn(now); break;
        }
    }

    // D-pad left / right also changes the radio. While the prompt is up (and for a moment after your
    // answer) the station you had is put straight back, so the D-pad only answers the officer.
    void GuardRadio(int now)
    {
        if (!radioSaved) return;
        if (stage != Stage.Ticket && now > radioGuardUntil) { radioSaved = false; return; }
        if (now < radioNextFix) return;
        try
        {
            // a station change only lands a moment after the button press, so "retuning" is undone as well
            bool retuning = false;
            try { retuning = Function.Call<bool>("IS_RADIO_RETUNING"); }
            catch (Exception) { }
            if (retuning || Game.RadioStation != savedRadio)
            {
                Game.RadioStation = savedRadio;
                radioNextFix = now + 250;
            }
        }
        catch (Exception ex) { radioSaved = false; LogOnce("radio", "Radio lock unavailable: " + ex.Message); }
    }

    // ----------------------------------------------------------------- detection

    void TickIdle(int now)
    {
        Player pl = Game.LocalPlayer;
        Ped pp = pl.Character;
        if (!Ok(pp) || !pp.isAlive) { speedingSince = 0; return; }

        int wanted = pl.WantedLevel;

        if (!pp.isInVehicle())
        {
            speedingSince = 0;
            return;
        }

        if (now < cooldownUntil || wanted != 0) { speedingSince = 0; return; }

        Vehicle pv = pp.CurrentVehicle;
        if (!Ok(pv)) { speedingSince = 0; return; }
        Model m = pv.Model;
        if (m.isBoat || m.isHelicopter || IsPoliceCar(pv) || PlayerInPoliceVehicle(pp, pv)) { speedingSince = 0; return; }
        if (pv.isSeatFree(VehicleSeat.Driver) || pv.GetPedOnSeat(VehicleSeat.Driver) != pp) { speedingSince = 0; return; }

        float mph = pv.Speed * 2.23694f;
        if (mph > SpeedLimitMph) { if (speedingSince == 0) speedingSince = now; }
        else speedingSince = 0;

        if (speedingSince == 0 || now - speedingSince < SpeedingHoldMs) return;
        if (now < nextIdleCheck) return;
        nextIdleCheck = now + 250;
        if (MissionActive()) return;

        Vehicle cop = FindSquadCar(pv.Position, WitnessRadius, pv);
        if (!Ok(cop)) return;
        // the squad car that saw you decides ONCE whether it bothers with you
        CopDecision d = FindDecision(cop, now);
        if (d == null)
        {
            d = new CopDecision();
            d.Car = cop;
            d.Stops = rng.Next(100) < StopChancePct;
            d.Until = now + 60000;
            copDecisions.Add(d);
            Log("A squad car saw you speeding: " + (d.Stops ? "it pulls you over" : "it lets you go") + " (" + StopChancePct + "% chance)");
        }
        if (d.Stops) BeginSpeedingStop(cop, pv, now);
    }

    Vehicle FindSquadCar(Vector3 pos, float radius, Vehicle exclude)
    {
        Vehicle best = null;
        float bestDist = radius;
        foreach (Vehicle v in World.GetAllVehicles())
        {
            if (!Ok(v) || !v.isAlive || !IsPoliceCar(v)) continue;
            if (Ok(exclude) && v == exclude) continue;
            CopDecision dec = FindDecision(v, Game.GameTime);
            if (dec != null && !dec.Stops) continue;      // this one already let you go
            Vector3 vp = v.Position;
            if (Math.Abs(vp.Z - pos.Z) > 8f) continue;
            float d = Dist2D(vp, pos);
            if (d > bestDist) continue;

            if (v.isSeatFree(VehicleSeat.Driver)) continue;
            Ped drv = v.GetPedOnSeat(VehicleSeat.Driver);
            if (!Ok(drv) || !drv.isAlive) continue;
            best = v;
            bestDist = d;
        }
        return best;
    }

    // everything the stop will need is asked for one item at a time over the next few seconds,
    // instead of all in the frame the lights come on. Props load only when an officer needs them.
    void PreloadStopAnims()
    {
        preloadQueue.Clear();
        preloadQueue.Add(CopCarAnimSet);
        preloadQueue.Add(PlayerIntroSet);
        preloadQueue.Add(PlayerOutroSet);
        preloadQueue.Add(BikePaySet);
        preloadQueue.Add(CopTakeSet);
        preloadQueue.Add(WriteSet);
        preloadQueue.Add(PartnerRadioSet);
        preloadQueue.Add(PartnerIdleSet);
        preloadNextAt = Game.GameTime + 2500;
    }

    void TickPreload(int now)
    {
        if (preloadQueue.Count == 0 || now < preloadNextAt) return;
        preloadNextAt = now + 400;
        string name = preloadQueue[0];
        preloadQueue.RemoveAt(0);
        try { Function.Call("REQUEST_ANIMS", name); }
        catch (Exception) { }
    }

    // ----------------------------------------------------------------- speeding: start

    void BeginSpeedingStop(Vehicle cop, Vehicle pv, int now)
    {
        copCar = cop;
        playerVeh = pv;
        reason = "Speeding";
        playerIsBike = pv.Model.isBike;
        officers.Clear();

        TakeOfficer(cop.GetPedOnSeat(VehicleSeat.Driver), true, true);
        if (!cop.isSeatFree(VehicleSeat.RightFront))
        {
            Ped p2 = cop.GetPedOnSeat(VehicleSeat.RightFront);
            if (Ok(p2) && p2.isAlive) TakeOfficer(p2, false, true);
        }

        cop.isRequiredForMission = true;
        cop.SirenActive = true;
        episodeFolder = DetectEpisodeFolder();

        // the star is shown, but the game's own police AI is held back so only this car deals with you
        PoliceIgnore(true);
        if (ShowStarDuringStop) Game.LocalPlayer.WantedLevel = 1;
        HoldBackup(true);

        pullOverSaid = false;
        steering = false;
        caughtAt = rng.Next(100) < CaughtPct ? now + CaughtDelayMs : 0;
        Vector3 dims = pv.Model.GetDimensions();
        playerIsTruck = !playerIsBike && dims.Z > TruckMinHeight;
        Log("Your vehicle: " + dims.X.ToString("0.00") + " m wide, " + dims.Y.ToString("0.00") + " m long, " + dims.Z.ToString("0.00") + " m high" + (playerIsTruck ? " (truck animations)" : ""));
        PreloadStopAnims();

        speedingSince = 0;
        nextRetask = 0;
        placedAt = 0;
        outOfViewSince = 0;
        carArrest = false;
        uTurned = false;
        vanillaChasing = false;
        uTurning = false;
        uTurnCooldown = 0;
        lastDriveAt = 0;
        driveMode = 0;
        copStalledSince = 0;
        playerStoppedSince = 0;
        bothStoppedSince = 0;
        parked = false;
        stoppedOnce = false;
        followDeadline = now + PullOverMs;
        stopStartedAt = now;
        searchRolled = false;
        resumeNoRadio = false;
        stage = Stage.Follow;
        Log("SPEEDING STOP started: " + officers.Count + " officer(s), " + (playerIsBike ? "motorcycle" : "car") + ", episode folder " + episodeFolder);
    }

    // ----------------------------------------------------------------- end of a stop

    // while the stop is running no other police are sent or spawned; switched back the moment the script lets go
    void HoldBackup(bool on)
    {
        if (on == backupHeld) return;
        backupHeld = on;
    }

    // Backup during a stop: other squad cars that come at you with their lights on are sent back on patrol.
    // Nothing global is switched off (that is what stopped the ticket car from working).
    int nextBackupCheck;
    void TickBackup(int now)
    {
        if (!backupHeld || now < nextBackupCheck) return;
        nextBackupCheck = now + 700;
        try
        {
            Vector3 pos = Game.LocalPlayer.Character.Position;
            foreach (Model m in policeModels)
            {
                Vehicle[] cars = World.GetVehicles(pos, 150f, m);
                foreach (Vehicle v in cars)
                {
                    if (!Ok(v) || v == copCar || !v.SirenActive) continue;
                    Ped d = v.GetPedOnSeat(VehicleSeat.Driver);
                    if (!Ok(d) || !d.isAlive) continue;
                    v.SirenActive = false;
                    d.Task.CruiseWithVehicle(v, 12f, true);
                }
            }
        }
        catch (Exception ex) { LogOnce("backup", "Backup check failed: " + ex.Message); }
    }

    // you got away: no star, the stop is over, the squad car goes back to patrolling
    void Evaded()
    {
        Log("You evaded the traffic stop: script off, no wanted level");
        Ped drv = officers.Count > 0 ? officers[0].Ped : null;
        Vehicle car = copCar;
        afterAt = 0;
        PoliceIgnore(false);
        Release();
        try { Game.LocalPlayer.WantedLevel = 0; }
        catch (Exception) { }
        try
        {
            if (Ok(car)) car.SirenActive = false;
            if (Ok(drv) && Ok(car) && drv.isInVehicle()) drv.Task.CruiseWithVehicle(car, 15f, true);
        }
        catch (Exception) { }
        cooldownUntil = Game.GameTime + 15000;
    }

    void Release()
    {
        HoldBackup(false);
        if (holdFire)
        {
            holdFire = false;
            foreach (Officer o in officers)
            {
                if (Ok(o.Ped)) TryNative("SET_CHAR_SHOOT_RATE", o.Ped, 50);
            }
        }
        RemoveRadio();
        StopPlayerAnim();
        StopWriting(false);
        copTakeAt = 0;
        foreach (Officer o in officers)
        {
            if (!Ok(o.Ped)) continue;
            try
            {
                if (gesturesBlocked) TryNative("BLOCK_CHAR_GESTURE_ANIMS", o.Ped, false);
                o.Ped.Task.ClearAll();
                o.Ped.Task.AlwaysKeepTask = false;
                o.Ped.BlockPermanentEvents = false;
                o.Ped.NoLongerNeeded();
            }
            catch (Exception) { }
        }
        officers.Clear();
        if (Ok(copCar))
        {
            // the script switched these lights on, so it switches them off when it lets go; police that
            // are really chasing you turn their own back on
            try { copCar.SirenActive = false; }
            catch (Exception) { }
            try { copCar.NoLongerNeeded(); }
            catch (Exception) { }
        }
        copCar = null;
        playerVeh = null;
        steering = false;
        gesturesBlocked = false;
        stage = Stage.Idle;
    }

    // script lets go; the vanilla 1-star wanted system deals with the player from here
    void Handover(string why)
    {
        Log("Handing over to the vanilla wanted system: " + why);
        afterAt = 0;
        PoliceIgnore(false);
        if (Game.LocalPlayer.WantedLevel < 1) Game.LocalPlayer.WantedLevel = 1;
        Release();
    }

    // ticket paid
    void Finish(int now)
    {
        Log("Stop finished peacefully");
        PoliceIgnore(false);
        if (Ok(copCar))
        {
            try { copCar.SirenActive = false; }
            catch (Exception) { }
            TryNative("SET_SIREN_WITH_NO_DRIVER", copCar, false);
            if (officers.Count > 0 && Ok(officers[0].Ped) && officers[0].Ped.isInVehicle())
                TryNative("TASK_CAR_DRIVE_WANDER", officers[0].Ped, copCar, 12f, 1);
        }
        else
        {
            // foot patrol goes back to walking the beat
            foreach (Officer o in officers)
            {
                if (!Ok(o.Ped)) continue;
                try
                {
                    o.Ped.Task.ClearAll();
                    o.Ped.Task.AlwaysKeepTask = false;
                    o.Ped.BlockPermanentEvents = false;
                    o.Ped.Task.WanderAround();
                    o.Ped.NoLongerNeeded();
                }
                catch (Exception) { }
            }
            officers.Clear();
        }
        Release();
        cooldownUntil = now + 15000;
    }

    bool CommonChecks(int now)
    {
        Player pl = Game.LocalPlayer;
        Ped pp = pl.Character;
        bool peaceful = stage == Stage.Return && peacefulReturn;

        if (!Ok(pp) || !pp.isAlive)
        {
            PoliceIgnore(false);
            Release();
            return false;
        }

        if (peaceful)
        {
            // your ticket is paid: you are free to go, the script only finishes the officers' business
            bool lost = !Ok(copCar);
            if (lost || pl.WantedLevel > 0) { PoliceIgnore(false); Release(); return false; }
            return true;
        }

        bool copsFine = Ok(copCar) && copCar.isAlive;
        foreach (Officer o in officers) if (!Ok(o.Ped) || !o.Ped.isAlive) copsFine = false;

        // anything beyond a ticket (2+ stars, dead officer, wrecked squad car) = vanilla wanted system
        if (!copsFine) { Handover("an officer or the squad car is gone"); return false; }
        if (pl.WantedLevel > 1) { Handover("wanted level went above 1 star"); return false; }

        // getting out of your car / bike = vanilla arrest
        if (!Ok(playerVeh) || !pp.isInVehicle() || pp.CurrentVehicle != playerVeh)
        {
            if (stage == Stage.Arrest)
            {
                // he has you out: he drops what he was doing on the spot (no climbing into your car)
                // and the vanilla 1-star arrest takes it from there
                if (!arrestOnFoot && !carArrest) BeginFootArrest(Game.GameTime);
                return true;
            }
            Handover("you left your vehicle");
            return false;
        }

        if (ShowStarDuringStop && pl.WantedLevel < 1) pl.WantedLevel = 1;
        return true;
    }

    // ----------------------------------------------------------------- speeding: following / parking

    // "Pull over!" - said once, when the squad car is close enough to be heard
    void TickPullOverLine(int now, float gap)
    {
        if (pullOverSaid) return;
        if (gap > PullOverSayDistance && now - stopStartedAt < 6000) return;
        pullOverSaid = true;
        OfficerSay(officers[0].Ped, "PullOver");
    }

    void TickFollow(int now)
    {
        Ped driver = officers[0].Ped;
        if (!driver.isInVehicle()) { Handover("the police driver left his car"); return; }

        float ps = playerVeh.Speed;
        Vector3 pvPos = playerVeh.Position;
        Vector3 copPos = copCar.Position;
        float gap = Dist2D(copPos, pvPos);
        float desired = DesiredCenterGap();

        if (gap > LoseDistance) { Handover("you outran the squad car"); return; }

        TickPullOverLine(now, gap);

        bool playerStopped = ps < 1.0f;
        if (playerStopped)
        {
            followDeadline = 0;
            if (playerStoppedSince == 0) playerStoppedSince = now;
        }
        else
        {
            // 15 s to pull over the first time; only 10 s if you already stopped once and drove off again
            if (followDeadline == 0) followDeadline = now + (stoppedOnce ? SecondChanceMs : PullOverMs);
            if (now > followDeadline) { Handover("you did not pull over in time"); return; }
            playerStoppedSince = 0;
            bothStoppedSince = 0;
            parked = false;
        }

        // Number one job: get behind you. The game's driver will not turn round in the road - he follows his
        // lane away from you and goes round the block. So if the squad car is pointing away from you, the
        // script turns it round right there (a normal tight U-turn), then the game's driver takes it to you.
        if (uTurning) return;
        // for a few seconds after being put behind you, and only while you cannot see him, he is kept at
        // your speed so he is right there with you instead of dropping back
        if (placedAt != 0 && now - placedAt < 8000 && !parked && gap > desired + 6f && !CopCarOnScreen()
            && Math.Abs(BearingDiff(copCar, pvPos)) < 50f && copCar.Speed < ps + PlacedExtraSpeed)
        {
            copCar.Speed = ps + PlacedExtraSpeed;
        }
        // Catch-up: he has fallen well behind and is not gaining, and you have not been able to see him for
        // FlipDelayMs: he is put on the road behind you (out of your view), facing you and already rolling,
        // exactly as for the oncoming case below
        if (!parked && gap > CatchUpGapMeters && now > uTurnCooldown && !CopCarOnScreen())
        {
            if (farHiddenSince == 0) { farHiddenSince = now; farGapAtStart = gap; }
            else if (now - farHiddenSince >= FlipDelayMs && gap > farGapAtStart - 8f)
            {
                Log("Squad car: " + gap.ToString("0") + " m back, out of view and not catching up");
                farHiddenSince = 0;
                FlipToPlayer(now);
                return;
            }
        }
        else farHiddenSince = 0;
        if (UTurnToYou && !parked && gap > 12f && now > uTurnCooldown)
        {
            float away = BearingDiff(copCar, pvPos);
            if (copCar.Speed < 1.0f && gap > 25f)
            {
                if (copStalledSince == 0) copStalledSince = now;
            }
            else copStalledSince = 0;
            // ONLY the head-on case: his front bumper and yours pointing in opposite directions (he was
            // oncoming and you drove past him). A cop sideways-on to you, behind you, or stuck is left alone.
            float headDiff = copCar.Heading - playerVeh.Heading;
            while (headDiff > 180f) headDiff -= 360f;
            while (headDiff < -180f) headDiff += 360f;
            bool headOn = Math.Abs(headDiff) > 140f;
            // he has to have been out of your sight for FlipDelayMs in one go before anything happens
            bool wantFlip = Math.Abs(away) > 105f && headOn && !CopCarOnScreen();
            if (!wantFlip) outOfViewSince = 0;
            else if (outOfViewSince == 0) outOfViewSince = now;
            if (wantFlip && now - outOfViewSince >= FlipDelayMs)
            {
                // out of your camera's view and pointing away: turned round to face you on the spot
                FlipToPlayer(now);
                return;
            }
            if (false && Math.Abs(away) > 105f)   // (the driver-performed turn is switched off: nothing makes him turn while you can see him)
            {
                uTurning = true;
                uTurnDir = Math.Abs(away) > 150f ? 1f : (away > 0f ? 1f : -1f);   // straight behind him: turn left, across the road
                uTurnStart = now;
                uQuatSign = 1f;
                uFrames = 0;
                uLastHeading = copCar.Heading;
                lastSteerAt = now;
                try { driver.Task.ClearAll(); }
                catch (Exception) { }
                uActIdx = 0;
                uSwapped = false;
                Log("Squad car: pointing away from you, driver is told to turn round");
                UTurnOrder(now);
                return;
            }
        }

        // Close behind you at low speed the squad car is steered by the script instead of the game's
        // traffic AI: it lines itself up on your car and follows your exact line, so it ends up
        // parked straight behind you wherever you stop (kerb included) instead of wandering.
        Vector3 spot = playerVeh.GetOffsetPosition(new Vector3(0f, -desired, 0f));
        float pfx, pfy;
        Forward(playerVeh.Heading, out pfx, out pfy);
        float bx = spot.X - copPos.X, by = spot.Y - copPos.Y;
        float along = bx * pfx + by * pfy;               // + = the spot is ahead of the squad car
        float lateral = Math.Abs(bx * pfy - by * pfx);
        float spotDist = (float)Math.Sqrt(bx * bx + by * by);
        // Once you have stopped and the squad car is near and behind you, the script drives it the last few
        // metres itself: it rolls onto your car's line like a real car (turning only as fast as it moves,
        // backing up for a second go if it comes in crooked) and stops straight behind your bumper.
        float hx, hy;
        Forward(copCar.Heading, out hx, out hy);
        bool pointsAtYou = gap > 0.1f && ((pvPos.X - copPos.X) * hx + (pvPos.Y - copPos.Y) * hy) / gap > 0.2f;
        bool canSteer = LineUpBehindYou && playerStopped && playerStoppedSince != 0 && now - playerStoppedSince > 400
            && gap < SteerRange && Math.Abs(pvPos.Z - copPos.Z) < 3f
            && (steering || (along > 1.0f && lateral < 12f && pointsAtYou));
        if (canSteer != steering)
        {
            steering = canSteer;
            if (steering) { driver.Task.ClearAll(); lineDone = false; lineReverse = false; lineTries = 0; }
            else nextRetask = 0;
            Log(steering ? "Squad car: lining up directly behind you" : "Squad car: back to normal driving");
        }

        if (steering)
        {
            if (!parked && playerStopped && lineDone)
            {
                parked = true;
                stoppedOnce = true;
                bothStoppedSince = now;
                Log("Both stopped, officers wait " + WaitBeforeExitMs + " ms");
            }
            else if (parked && now - bothStoppedSince >= WaitBeforeExitMs)
            {
                BeginApproach(now);
            }
            return;
        }

        if (!parked && VanillaChase)
        {
            // Getting to you is left to the game's own 1-star police driving (the one that turns round and
            // comes straight at you). The script only takes the car over for the last stretch, to stop
            // behind your bumper instead of ramming you.
            // distance between his front bumper and your rear bumper
            float bumperGap = gap - (desired - ParkGapMeters);
            bool want = vanillaChasing ? bumperGap > VanillaTakeover : bumperGap > VanillaTakeover + 7f;
            if (want != vanillaChasing) SetVanillaChase(want);
            if (vanillaChasing) return;
        }

        if (!parked)
        {
            if (now >= nextRetask)
            {
                nextRetask = now + 600;
                Vector3 target = spot;
                // match the player's pace; a little extra when far behind, a little less when close
                float speed = Math.Max(4f, ps + Clamp((gap - desired - 3f) * 0.6f, -2f, 14f));
                // after a U-turn he is allowed to use any lane, otherwise the game's driver would turn away again
                if (uTurned) driver.Task.DriveTo(target, speed, false, true);
                else driver.Task.DriveTo(target, speed, false);
            }

            // never close in faster than the remaining gap allows - follows, does not ram
            float cx, cy;
            Forward(copCar.Heading, out cx, out cy);
            float tx = pvPos.X - copPos.X, ty = pvPos.Y - copPos.Y;
            bool facingPlayer = gap > 0.1f && (tx * cx + ty * cy) / gap > 0.5f;
            float cap = ps + Math.Max(0f, gap - desired) * 1.2f;
            if (gap < 30f && facingPlayer && copCar.Speed > cap + 0.5f) copCar.Speed = cap;

            // you have stopped: creep the last few metres up to your bumper instead of stopping short
            if (playerStopped && facingPlayer && gap > desired + 0.4f && gap < desired + 10f && copCar.Speed < 1.6f)
                copCar.Speed = 1.8f;

            // officers only get out once the squad car is 2 metres or less behind you; no time limit on getting there
            bool closeEnough = gap <= desired - ParkGapMeters + 2.0f && facingPlayer;
            bool stuck = false;
            if (playerStopped && (closeEnough || stuck))
            {
                parked = true;
                stoppedOnce = true;
                driver.Task.ClearAll();
                copCar.Speed = 0f;
                bothStoppedSince = now;
                Log("Both stopped, officers wait " + WaitBeforeExitMs + " ms");
            }
        }
        else if (now - bothStoppedSince >= WaitBeforeExitMs)
        {
            BeginApproach(now);
        }
    }

    void SetVanillaChase(bool on)
    {
        vanillaChasing = on;
        Log(on ? "Squad car: game's own police pursuit is driving" : "Squad car: close, pulling up behind you");
        PoliceIgnore(!on);
        foreach (Officer o in officers)
        {
            if (!Ok(o.Ped)) continue;
            try
            {
                if (on)
                {
                    // fully let go of him: only an ordinary street cop gets the game's own pursuit driving
                    o.Ped.Task.ClearAll();
                    o.Ped.Task.AlwaysKeepTask = false;
                    o.Ped.BlockPermanentEvents = false;
                    o.Ped.NoLongerNeeded();
                }
                else
                {
                    o.Ped.isRequiredForMission = true;
                    o.Ped.BlockPermanentEvents = true;
                    o.Ped.Task.AlwaysKeepTask = true;
                }
            }
            catch (Exception) { }
        }
        try
        {
            if (Ok(copCar))
            {
                if (on) copCar.NoLongerNeeded();
                else copCar.isRequiredForMission = true;
            }
        }
        catch (Exception) { }
        lastDriveAt = 0;
        nextRetask = 0;
    }

    // degrees the squad car would have to turn to point at a position (+ = to its left)
    float BearingDiff(Vehicle car, Vector3 to)
    {
        Vector3 cp = car.Position;
        float want = (float)(Math.Atan2(-(to.X - cp.X), to.Y - cp.Y) * 180.0 / Math.PI);
        float diff = want - car.Heading;
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        return diff;
    }

    // The DRIVER turns the car round: he is given the game's own "steer hard and accelerate" driving action,
    // so the wheels turn and the car drives the U-turn itself. The script never moves or rotates the car.
    static readonly int[] UTurnLeftActions = { 7, 10, 4 };
    static readonly int[] UTurnRightActions = { 8, 11, 5 };

    void UTurnOrder(int now)
    {
        if (officers.Count == 0 || !Ok(officers[0].Ped) || !Ok(copCar)) return;
        int[] list = uTurnDir > 0f ? UTurnLeftActions : UTurnRightActions;
        int action = list[uActIdx % list.Length];
        uActCheckAt = now + 1300;
        uActHeading = copCar.Heading;
        Log("Squad car: U-turn driving action " + action);
        TryNative("TASK_CAR_TEMP_ACTION", officers[0].Ped, copCar, action, 9000);
    }

    bool CopCarOnScreen()
    {
        // The game's own "is it on screen" answer is not trusted alone: the car also counts as out of view
        // whenever it is outside the cone your camera is looking down (behind you, or well off to the side).
        bool flag = true;
        try { flag = copCar.isOnScreen; }
        catch (Exception) { }
        if (!flag) return false;
        try
        {
            GTA.Camera cam = Game.CurrentCamera;
            Vector3 cpos = cam.Position;
            Vector3 dir = cam.Direction;
            Vector3 cp = copCar.Position;
            float vx = cp.X - cpos.X, vy = cp.Y - cpos.Y;
            float vl = (float)Math.Sqrt(vx * vx + vy * vy);
            float dl = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (vl > 1f && dl > 0.01f)
            {
                float cos = (vx * dir.X + vy * dir.Y) / (vl * dl);
                if (cos < 0.5f) return false;      // more than 60 degrees off the centre of your view
            }
        }
        catch (Exception ex) { LogOnce("camcheck", "Camera check failed: " + ex.Message); }
        return true;
    }

    // is a spot inside the cone your camera is looking down?
    bool PointInView(Vector3 p)
    {
        try
        {
            GTA.Camera cam = Game.CurrentCamera;
            Vector3 cpos = cam.Position;
            Vector3 dir = cam.Direction;
            float vx = p.X - cpos.X, vy = p.Y - cpos.Y;
            float vl = (float)Math.Sqrt(vx * vx + vy * vy);
            float dl = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
            if (vl > 1f && dl > 0.01f) return (vx * dir.X + vy * dir.Y) / (vl * dl) >= 0.5f;
        }
        catch (Exception) { }
        return true;
    }

    // Only ever used while the squad car is NOT on your screen. A squad car that is pointing away from you
    // (or stuck somewhere) is put on the road about 35 m BEHIND you, facing you and already rolling - the
    // one situation in which the game's driver follows properly. If there is no usable spot behind you
    // that is also out of your view, it is only turned to face you where it stands.
    void FlipToPlayer(int now)
    {
        bool placed = false;
        try
        {
            Vector3 pp = playerVeh.Position;
            Vector3 want = playerVeh.GetOffsetPosition(new Vector3(0f, -PlacedBehindMeters, 0f));
            Vector3 node = World.GetNextPositionOnStreet(want);
            float d = Dist2D(node, pp);
            bool usable = (node.X != 0f || node.Y != 0f) && d > PlacedBehindMeters * 0.55f && d < PlacedBehindMeters + 30f && Math.Abs(node.Z - pp.Z) < 8f && !PointInView(node);
            if (usable)
            {
                if (officers.Count > 0 && Ok(officers[0].Ped)) officers[0].Ped.Task.ClearAll();
                float h = (float)(Math.Atan2(-(pp.X - node.X), pp.Y - node.Y) * 180.0 / Math.PI);
                while (h < 0f) h += 360f;
                while (h >= 360f) h -= 360f;
                // moved with the lightest call there is, and without the game's "settle the car on the ground"
                // step (the likely cause of the one-second hitch): he is set just above the road and drops onto it
                Function.Call("SET_CAR_COORDINATES_NO_OFFSET", copCar, node.X, node.Y, node.Z + 0.75f);
                copCar.Heading = h;
                copCar.Speed = Math.Max(6f, playerVeh.Speed - 2f);
                placed = true;
                placedAt = now;
                uTurned = false;      // he is behind you now: plain normal following
                Log("Squad car: out of view and going the wrong way, put on the road " + d.ToString("0") + " m behind you");
            }
        }
        catch (Exception ex) { Log("Putting the squad car behind you failed: " + ex.Message); }

        if (!placed)
        {
            float diff = BearingDiff(copCar, playerVeh.Position);
            float nh = copCar.Heading + diff;
            while (nh < 0f) nh += 360f;
            while (nh >= 360f) nh -= 360f;
            try
            {
                if (officers.Count > 0 && Ok(officers[0].Ped)) officers[0].Ped.Task.ClearAll();
                copCar.Heading = nh;
                copCar.Speed = Math.Max(10f, playerVeh.Speed);
            }
            catch (Exception) { }
            uTurned = true;
            Log("Squad car: out of view and pointing away, turned to face you (no free spot behind you)");
        }
        uTurning = false;
        outOfViewSince = 0;
        copStalledSince = 0;
        uTurnCooldown = now + 4000;
        lastDriveAt = 0;
        nextRetask = 0;
    }

    // watches the driver's U-turn and ends it once he is pointing at you
    void UTurnFrame(int now)
    {
        lastSteerAt = now;
        if (!Ok(copCar) || !Ok(playerVeh) || officers.Count == 0 || !Ok(officers[0].Ped)) { uTurning = false; return; }
        float diff = BearingDiff(copCar, playerVeh.Position);

        // he left your view before finishing the turn: finish it for him right now
        if (Math.Abs(diff) >= 30f && !CopCarOnScreen()) { FlipToPlayer(now); return; }

        if (Math.Abs(diff) < 30f || now - uTurnStart > 10000)
        {
            uTurning = false;
            uTurned = true;
            uTurnCooldown = now + 8000;
            try { officers[0].Ped.Task.ClearAll(); }
            catch (Exception) { }
            lastDriveAt = 0;
            nextRetask = 0;      // straight back to the normal "pull up behind you" driving
            Log("Squad car: " + (Math.Abs(diff) < 30f ? "turned round, coming to you" : "could not turn round in time, normal driving"));
            return;
        }

        // is the action he was given actually turning the car, and the right way?
        if (now >= uActCheckAt)
        {
            float moved = copCar.Heading - uActHeading;
            while (moved > 180f) moved -= 360f;
            while (moved < -180f) moved += 360f;
            Log("Squad car: turned " + moved.ToString("0") + " degrees in the last 1.3 s");
            if (Math.Abs(moved) < 6f)
            {
                uActIdx++;            // that action does not turn the car: try the next one
                UTurnOrder(now);
            }
            else if (moved * uTurnDir < 0f && !uSwapped)
            {
                uSwapped = true;      // it turns the other way round: use its mirror image
                uTurnDir = -uTurnDir;
                UTurnOrder(now);
                uTurnDir = -uTurnDir;
            }
            else
            {
                uActCheckAt = now + 1300;
                uActHeading = copCar.Heading;
            }
        }
    }

    // runs every frame while the script is lining the squad car up behind you
    void SteerFrame(int now)
    {
        int dtMs = now - lastSteerAt;
        lastSteerAt = now;
        if (dtMs <= 0) return;
        if (dtMs > 100) dtMs = 100;
        if (!Ok(copCar) || !Ok(playerVeh)) return;
        if (lineDone) { copCar.Speed = 0f; return; }

        float ph = playerVeh.Heading;
        float pfx, pfy;
        Forward(ph, out pfx, out pfy);
        Vector3 cp = copCar.Position;
        Vector3 spot = playerVeh.GetOffsetPosition(new Vector3(0f, -DesiredCenterGap(), 0f));
        float rx = cp.X - spot.X, ry = cp.Y - spot.Y;
        float s = -(rx * pfx + ry * pfy);                 // metres still to go along your car's line
        float lat = Math.Abs(rx * pfy - ry * pfx);        // metres off that line
        float cur = copCar.Heading;
        float offH = ph - cur;
        while (offH > 180f) offH -= 360f;
        while (offH < -180f) offH += 360f;

        float wantH;
        float speed;
        if (lineReverse)
        {
            // back straight up, squaring to your car, then come in again
            if (s >= 9f || now > lineReverseUntil) { lineReverse = false; return; }
            wantH = ph;
            speed = -2.0f;
        }
        else
        {
            bool crooked = lat > 0.5f || Math.Abs(offH) > 8f;
            bool cannotMakeIt = s < 6f && lat > 0.3f + s * 0.35f;
            if (s <= 0.25f || cannotMakeIt)
            {
                if ((cannotMakeIt || crooked) && lineTries < 3)
                {
                    lineTries++;
                    lineReverse = true;
                    lineReverseUntil = now + 4000;
                    return;
                }
                lineDone = true;
                copCar.Speed = 0f;
                return;
            }
            // aim at a point on your car's line a few metres ahead of the squad car
            float look = Clamp(s * 0.6f, 2.5f, 5f);
            float t = Math.Max(s - look, -2f);
            float ax = spot.X - pfx * t - cp.X, ay = spot.Y - pfy * t - cp.Y;
            wantH = (float)(Math.Atan2(-ax, ay) * 180.0 / Math.PI);
            speed = Clamp(0.8f + s * 0.5f, 1.0f, 3.5f);
        }

        float diff = wantH - cur;
        while (diff > 180f) diff -= 360f;
        while (diff < -180f) diff += 360f;
        // a car can only turn as far as it rolls: 5.5 m turning radius
        float maxTurn = (float)(Math.Abs(speed) * dtMs / 1000.0 / 5.5 * 180.0 / Math.PI);
        float h = cur + Clamp(diff, -maxTurn, maxTurn);
        while (h < 0f) h += 360f;
        while (h >= 360f) h -= 360f;

        copCar.Heading = h;
        copCar.Speed = speed;
    }

    // ----------------------------------------------------------------- speeding: officers walk up

    void BeginApproach(int now)
    {
        stopPos = playerVeh.Position;
        steering = false;
        try { copCar.Speed = 0f; }
        catch (Exception) { }
        // nothing is forced here: once the driver is out, the game keeps the lights flashing without the siren sound

        foreach (Officer o in officers)
        {
            if (playerIsBike)
            {
                // driver: about 1 ft off the side of the bike. partner: 4 ft ahead of it, 2 ft off-centre
                o.Post = o.IsDriver
                    ? playerVeh.GetOffsetPosition(new Vector3(-0.9f, 0f, 0f))
                    : playerVeh.GetOffsetPosition(new Vector3(0.61f, 2.3f, 0f));
            }
            else
            {
                // driver's door / passenger's door
                o.Post = playerVeh.GetOffsetPosition(o.IsDriver
                    ? new Vector3(-(playerVeh.Model.GetDimensions().X * 0.5f + CopDoorGap), DoorForwardOffset, 0f)
                    : new Vector3(1.55f, 0.35f, 0f));
            }
            TryNative("TASK_LEAVE_CAR", o.Ped, copCar);
            o.Step = 0;
            o.StepAt = now;
        }
        stage = Stage.Approach;
        Log("Officers getting out");
    }

    bool PlayerDroveOff()
    {
        return playerVeh.Speed > 3f || Dist2D(playerVeh.Position, stopPos) > 4f;
    }

    void CarOfficerStep(Officer o, int now)
    {
        Ped pp = Game.LocalPlayer.Character;
        if (o.Step == 0)
        {
            if (!o.Ped.isInVehicle() || now - o.StepAt > 5000)
            {
                WalkTo(o.Ped, o.Post, false);
                o.Step = 1;
                o.StepAt = now;
            }
        }
        else if (o.Step == 1)
        {
            if (Dist2D(o.Ped.Position, o.Post) < 0.35f || now - o.StepAt > 12000)
            {
                // the officer at a car door is put exactly on his mark so his hand meets the car
                if (o.IsDriver && !playerIsBike)
                {
                    Vector3 cur = o.Ped.Position;
                    if (Dist2D(cur, o.Post) < 2.5f)
                        TryNative("SET_CHAR_COORDINATES_NO_OFFSET", o.Ped, o.Post.X, o.Post.Y, cur.Z);
                }
                TryNative("TASK_TURN_CHAR_TO_FACE_CHAR", o.Ped, pp);
                o.Step = 2;
                o.StepAt = now;
            }
        }
        else if (o.Step == 2 && now - o.StepAt > 1200)
        {
            Arrive(o, pp, now);
        }
    }

    void TickApproach(int now)
    {
        // took off while they were walking up: they run back to the car, then vanilla takes over
        if (PlayerDroveOff()) { BeginReturn(false, now); return; }
        foreach (Officer o in officers) CarOfficerStep(o, now);
        TickPartner(now);
        if (officers[0].Step == 3)
        {
            // Pusher installed: it may turn this stop into a search (it rolls the chance itself)
            if (!searchRolled)
            {
                searchRolled = true;
                if (TryPusherSearch(now)) return;
            }
            BeginTicket(now);
        }
    }

    // ----------------------------------------------------------------- ticket prompt

    void BeginTicket(int now)
    {
        Ped cop = officers[0].Ped;
        // no hand-waving while he talks at your vehicle: only the script's animation moves his arms (his mouth still moves)
        if (BlockTalkGestures)
        {
            foreach (Officer o in officers) TryNative("BLOCK_CHAR_GESTURE_ANIMS", o.Ped, true);
            gesturesBlocked = true;
        }
        writePending = false;
        writeFrozen = false;
        copTakeAt = 0;
        copTookAt = 0;
        putAwayPending = false;
        denyLatch = false;
        acceptLatch = false;

        if (playerIsBike)
        {
            StartWriting(cop, now);
            paperWanted = false;
        }
        else
        {
            // square him up to the door so the hand-on-car pose lines up, then start both animations together
            try
            {
                float h = playerVeh.Heading - 90f + CopCarHeadingOffset;
                while (h < 0f) h += 360f;
                while (h >= 360f) h -= 360f;
                cop.Heading = h;
            }
            catch (Exception) { }
            if (playerIsTruck)
            {
                // trucks and vans: the same ticket-writing animation (and ticket) as at a motorcycle
                StartWriting(cop, now);
                paperWanted = false;
            }
            else
            {
                Anim(cop, CopCarAnimSet, CopCarAnim, LoopFlag);
                carPoseAt = CopCarHoldAfterMs > 0 ? now + CopCarHoldAfterMs : 0;
            }
            playerLoopAnim = PlayerLoopCar;   // the car animations in every car, truck and van
            PlayerVehAnim(PlayerIntroSet, PlayerIntroCar, false);
            playerIntroPending = true;
            playerIntroSeen = false;
            playerIntroStart = now;
        }

        // he is at your window / bike / in front of you: this is when he speaks and the prompt appears
        OfficerSay(cop, reason);   // the "Speeding" line from replies.txt
        ticketDeadline = now + TicketAnswerMs;
        radioSaved = false;
        {
            try { savedRadio = Game.RadioStation; radioSaved = true; }
            catch (Exception) { }
        }
        stage = Stage.Ticket;
        Log("Ticket prompt shown (" + reason + ")");
    }

    void TickTicket(int now)
    {
        if (PlayerDroveOff()) { BeginReturn(false, now); return; }

        TickPartner(now);
        TickWriting(now);
        TickPlayerIntro(now);

        // said nothing for 15 seconds: arrested the vanilla way
        if (now > ticketDeadline) { Handover("no answer within " + TicketAnswerMs + " ms"); return; }

        bool deny = denyLatch;
        bool accept = acceptLatch;
        denyLatch = false;
        acceptLatch = false;
        if (!deny && !accept) return;
        radioGuardUntil = now + 6000;

        Player pl = Game.LocalPlayer;
        Ped pp = pl.Character;
        Ped cop = officers[0].Ped;
        bool wantsAccept = accept && !deny;
        // can't cover the ticket: you say your AcceptArrest line and the officer arrests you
        accepted = wantsAccept && pl.Money >= TicketCost;
        string folder = !wantsAccept ? "deny" : (accepted ? "accept" : "AcceptArrest");
        Log("Player chose: " + folder);
        int len = PlayPlayerLine(folder);

        if (accepted)
        {
            {
                if (writeFrozen || writePending)
                {
                    // he is holding the ticket (motorcycle / truck / van): the animation runs on to its end
                    // so he puts it away, and only then does the money change hands
                    writePending = false;
                    if (writeFrozen) AnimSpeed(cop, WriteSet, WriteAnim, 1.0f);
                    writeFrozen = false;
                    putAwayPending = true;
                    putAwayStart = now;
                }
                else
                {
                    StopWriting(false);
                    StartMoneyHandover(now);
                }
            }
        }
        else
        {
            // denied or can't pay: the friendly animations end right away
            StopPlayerAnim();
            StopWriting(false);
            try { cop.Task.ClearAll(); }
            catch (Exception) { }
        }

        acceptChosenAt = now;
        replyStep = 0;
        // deny / can't pay: fixed 3 second window (you can drive off in it). accept: as long as your line takes.
        noCashReply = wantsAccept && !accepted;
        if (noCashReply)
        {
            // accepted but can't pay: your line plays and the script is finished right there. Nothing is told
            // to the officers; they are ordinary 1-star police from this moment.
            // (the shut-off itself happens in TickReply, NoCashArrestMs after your line has finished)
        }
        replyAt = now + (accepted ? Math.Max(len + 300, 1500) : (noCashReply ? Math.Max(len, 0) + NoCashArrestMs : DenyArrestMs));
        stage = Stage.Reply;
    }

    // you hand the money over; a moment later he takes it
    void StartMoneyHandover(int now)
    {
        if (playerIsBike) PlayerVehAnim(BikePaySet, BikePayAnim(playerVeh), false);
        else PlayerVehAnim(PlayerOutroSet, PlayerOutroAnim, false);
        if (CopTakeDelayMs <= 0)
        {
            Anim(officers[0].Ped, CopTakeSet, CopTakeAnim, AnimationFlags.None);
            copTookAt = now;
        }
        else copTakeAt = now + CopTakeDelayMs;
    }

    void TickReply(int now)
    {
        if (PlayerDroveOff())
        {
            if (accepted && replyStep >= 1) BeginReturn(true, now);   // already paid - free to go
            else BeginReturn(false, now);
            return;
        }
        TickPartner(now);
        TickWriting(now);

        Ped cop = officers[0].Ped;
        if (putAwayPending)
        {
            // wait for the ticket animation to finish: the paper goes as his hand reaches his pocket
            int el = now - putAwayStart;
            float t = AnimTime(cop, WriteSet, WriteAnim);
            bool playing = AnimPlaying(cop, WriteSet, WriteAnim);
            if (t >= PaperGoneAt || el > 400 && !playing) RemovePaper();
            if (t >= 0.97f || (el > 400 && !playing) || el > PutAwayMaxMs)
            {
                putAwayPending = false;
                StopWriting(false);
                StartMoneyHandover(now);
            }
        }
        if (copTakeAt != 0 && now >= copTakeAt)
        {
            copTakeAt = 0;
            Anim(cop, CopTakeSet, CopTakeAnim, AnimationFlags.None);
            copTookAt = now;
        }
        if (now < replyAt) return;

        if (replyStep == 0)
        {
            if (noCashReply)
            {
                StopWriting(false);
                BeginCarArrest(now);
                return;
            }
            int len = OfficerSay(cop, accepted ? "Accept" : "Deny");
            if (!accepted)
            {
                BeginArrest(now);
                return;
            }

            Player pl = Game.LocalPlayer;
            pl.Money = Math.Max(0, pl.Money - TicketCost);
            PoliceIgnore(false);
            pl.WantedLevel = 0;
            if (!putAwayPending) StopWriting(true);
            // a follow-up remark some of the time, about 5 seconds after you accepted (never on top of the officer's reply)
            // your follow-up remark (some of the time): 4 seconds after the officer takes the money
            if (rng.Next(100) < AcceptAfterPct)
            {
                if (putAwayPending) afterAt = now + PutAwayMaxMs + CopTakeDelayMs + AcceptAfterTakeMs;
                else if (copTookAt != 0) afterAt = Math.Max(copTookAt + AcceptAfterTakeMs, now + 100);
                else if (copTakeAt != 0) afterAt = copTakeAt + AcceptAfterTakeMs;
                else afterAt = Math.Max(acceptChosenAt + AcceptAfterMs, now + len + 300);
            }
            else afterAt = 0;
            replyStep = 1;
            replyAt = now + len + 400;
            Log("Ticket paid: $" + TicketCost);
            return;
        }

        // he does not walk off until the ticket is away and the money has changed hands
        if (putAwayPending || copTakeAt != 0 || (copTookAt != 0 && now - copTookAt < 1800)) return;
        EndPeacefully(now);
    }

    // deny / can't pay: the officer at your door pulls you out of the vehicle. The moment you are out
    // (or you drive off, or it takes too long) the vanilla 1-star police take over and arrest you.
    // while you are being told to pull over: firing a gun or running over someone on foot ends the
    // traffic stop on the spot and the vanilla wanted system deals with you
    int nextCrimeCheck;
    string FollowCrime(int now)
    {
        Ped pp = Game.LocalPlayer.Character;
        try { if (pp.isShooting) return "you fired a weapon during the stop"; }
        catch (Exception) { }
        if (now < nextCrimeCheck || !Ok(playerVeh)) return null;
        nextCrimeCheck = now + 200;
        try
        {
            Ped[] near = World.GetPeds(playerVeh.Position, 12f);
            foreach (Ped p in near)
            {
                if (!Ok(p) || p == pp || p.isInVehicle()) continue;
                if (Function.Call<bool>("HAS_CHAR_BEEN_DAMAGED_BY_CAR", p, playerVeh)) return "you hit a pedestrian during the stop";
            }
        }
        catch (Exception ex) { LogOnce("crime", "Pedestrian-hit check failed: " + ex.Message); }
        return null;
    }

    // deny / can't pay: the traffic stop simply ends. The script lets go of the officers and the vanilla
    // 1-star wanted system arrests you; the officer at your door is told ONCE to arrest you, never again.
    void BeginArrestVanilla(int now)
    {
        Log("Ticket refused or not enough money: script ends, vanilla 1-star arrest");
        Ped doorCop = officers.Count > 0 ? officers[0].Ped : null;
        StopWriting(false);
        Handover("ticket refused or not enough money");
        if (Ok(doorCop))
        {
            try { Function.Call("TASK_CHAR_ARREST_CHAR", doorCop, Game.LocalPlayer.Character); }
            catch (Exception ex) { LogOnce("arresttask", "TASK_CHAR_ARREST_CHAR failed: " + ex.Message); }
        }
    }

    void BeginArrest(int now)
    {
        Log("Ticket refused or not enough money: officer is pulling you out");
        Ped cop = officers[0].Ped;
        StopWriting(false);
        PoliceIgnore(false);   // from here it is a real 1-star arrest as far as the game is concerned
        try
        {
            cop.Task.ClearAllImmediately();
            cop.Task.EnterVehicle(playerVeh, VehicleSeat.Driver);
        }
        catch (Exception ex) { Log("Arrest task failed: " + ex.Message); }
        arrestStart = now;
        arrestForced = false;
        arrestOnFoot = false;
        arrestPullSeen = false;
        stage = Stage.Arrest;
    }

    // you are out of the vehicle: the officer who pulled you out is ordered to ARREST you (the game's own
    // arrest task = busted), instead of being let go and left standing there aiming
    void BeginFootArrest(int now)
    {
        Log("You are out of the vehicle: officer is arresting you");
        arrestOnFoot = true;
        arrestFootStart = now;
        arrestFootTaskAt = 0;
        PoliceIgnore(false);
        if (Game.LocalPlayer.WantedLevel < 1) Game.LocalPlayer.WantedLevel = 1;
        foreach (Officer o in officers)
        {
            if (!Ok(o.Ped)) continue;
            try { o.Ped.Task.ClearAllImmediately(); }
            catch (Exception) { }
        }
        GiveArrestTask(now);
    }

    void GiveArrestTask(int now)
    {
        arrestFootTaskAt = now;
        Ped pp = Game.LocalPlayer.Character;
        // you come out of the car with empty hands: a 1-star cop arrests an unarmed suspect, but opens
        // fire on one holding a weapon
        TryNative("SET_CURRENT_CHAR_WEAPON", pp, 0, true);
        // only the officer who pulled you out makes the arrest; his partner stays out of it
        if (officers.Count > 0 && Ok(officers[0].Ped))
        {
            try { Function.Call("TASK_CHAR_ARREST_CHAR", officers[0].Ped, pp); }
            catch (Exception ex) { LogOnce("arresttask", "TASK_CHAR_ARREST_CHAR failed: " + ex.Message); }
            Log("Arrest order given to the door officer");
        }
        // his partner draws and covers you, nothing more
        if (officers.Count > 1 && Ok(officers[1].Ped))
        {
            try { officers[1].Ped.Task.AimAt(pp, 60000); }
            catch (Exception) { }
        }
        // guns out, but nobody fires unless you give them a reason (see TickFootArrest)
        holdFire = true;
        doorCopAimOnly = false;
        foreach (Officer o in officers)
        {
            if (Ok(o.Ped)) TryNative("SET_CHAR_SHOOT_RATE", o.Ped, 0);
        }
    }

    // Accepted without the money. Left to themselves the vanilla police draw, back off into cover and only
    // come for you several seconds later. So the two officers stay under the script for the arrest itself:
    // the one at your door is told once to arrest you and goes straight to it, his partner draws and covers.
    void BeginCarArrest(int now)
    {
        Log("No money: door officer arrests you");
        Ped pp = Game.LocalPlayer.Character;
        PoliceIgnore(false);
        if (Game.LocalPlayer.WantedLevel < 1) Game.LocalPlayer.WantedLevel = 1;
        try
        {
            Ped cop = officers[0].Ped;
            cop.Task.ClearAll();
            Function.Call("TASK_CHAR_ARREST_CHAR", cop, pp);
        }
        catch (Exception ex) { Log("Arrest order failed: " + ex.Message); }
        if (officers.Count > 1 && Ok(officers[1].Ped))
        {
            try { officers[1].Ped.Task.ClearAll(); officers[1].Ped.Task.AimAt(pp, 60000); }
            catch (Exception) { }
        }
        holdFire = true;
        foreach (Officer o in officers)
        {
            if (Ok(o.Ped)) TryNative("SET_CHAR_SHOOT_RATE", o.Ped, 0);
        }
        carArrest = true;
        arrestOnFoot = false;
        arrestStart = now;
        stage = Stage.Arrest;
    }

    void TickCarArrest(int now)
    {
        Player pl = Game.LocalPlayer;
        Ped pp = pl.Character;
        bool busted = false;
        try { busted = Function.Call<bool>("IS_PLAYER_BEING_ARRESTED"); }
        catch (Exception) { }
        if (busted) { arrestStart = now; return; }      // the arrest is playing: nothing is touched

        if (pp.isInVehicle() && PlayerDroveOff()) { BeginReturn(false, now); return; }

        // you aim at them or fire: the script lets go and they are ordinary police
        bool provoked = false;
        try { provoked = pp.isShooting; }
        catch (Exception) { }
        if (!provoked)
        {
            try
            {
                int pid = Function.Call<int>("GET_PLAYER_ID");
                foreach (Officer o in officers)
                {
                    if (Ok(o.Ped) && Function.Call<bool>("IS_PLAYER_FREE_AIMING_AT_CHAR", pid, o.Ped)) provoked = true;
                }
            }
            catch (Exception) { }
        }
        if (provoked) { Handover("you aimed at an officer or fired during the arrest"); return; }

        Ped cop = officers.Count > 0 ? officers[0].Ped : null;
        if (!Ok(cop) || cop.Position.DistanceTo(pp.Position) > 30f) { Handover("you got away from the arrest"); return; }
        // he has not managed it in 12 seconds: the vanilla police finish the job their own way
        if (now - arrestStart > 12000) { Handover("the door officer did not complete the arrest in time"); return; }
    }

    void TickFootArrest(int now)
    {
        Player pl = Game.LocalPlayer;
        Ped pp = pl.Character;
        bool busted = false;
        try { busted = Function.Call<bool>("IS_PLAYER_BEING_ARRESTED"); }
        catch (Exception) { }
        // he has you: nothing is touched while the game plays the arrest (break out of it and you get
        // 2 stars, which ends the script through the normal checks)
        if (busted) { arrestFootStart = now; return; }

        // the reasons they may open fire: you aim at one of them, or you fire a weapon (breaking out of
        // the arrest gives you 2 stars, which already ends the script). Then the script lets go completely.
        bool provoked = false;
        try { provoked = pp.isShooting; }
        catch (Exception) { }
        if (!provoked)
        {
            try
            {
                int pid = Function.Call<int>("GET_PLAYER_ID");
                foreach (Officer o in officers)
                {
                    if (!Ok(o.Ped)) continue;
                    if (Function.Call<bool>("IS_PLAYER_FREE_AIMING_AT_CHAR", pid, o.Ped)) provoked = true;
                }
            }
            catch (Exception ex) { LogOnce("aimcheck", "Aim check failed: " + ex.Message); }
        }
        if (provoked) { Handover("you aimed at an officer or fired during the arrest"); return; }

        // not provoked, so nobody shoots: an officer who fires anyway is put back to just covering you
        foreach (Officer o in officers)
        {
            if (!Ok(o.Ped)) continue;
            bool firing = false;
            try { firing = o.Ped.isShooting; }
            catch (Exception) { }
            if (!firing) continue;
            try { o.Ped.Task.ClearAll(); o.Ped.Task.AimAt(pp, 60000); }
            catch (Exception) { }
            if (o == officers[0] && !doorCopAimOnly) { doorCopAimOnly = true; Log("Door officer fired during the arrest: held to aiming only"); }
        }
        if (!pp.isAlive) { Handover("player died during the arrest"); return; }
        if (pl.WantedLevel < 1) pl.WantedLevel = 1;

        // ran away or got into a vehicle, or it is taking too long: normal 1-star chase
        if (pp.isInVehicle() && now - arrestFootTaskAt > 3000) { Handover("you got into a vehicle during the arrest"); return; }
        Ped cop = officers.Count > 0 ? officers[0].Ped : null;
        if (!Ok(cop) || cop.Position.DistanceTo(pp.Position) > 25f) { Handover("you ran from the arrest"); return; }
        if (now - arrestFootStart > 15000) { Handover("arrest took too long"); return; }

        // he is told to arrest you ONCE (when you came out of the vehicle), never again
    }

    void TickArrest(int now)
    {
        // leaving the vehicle (pulled out or by choice) is caught by the common checks -> vanilla arrest
        if (carArrest) { TickCarArrest(now); return; }
        if (arrestOnFoot) { TickFootArrest(now); return; }
        if (PlayerDroveOff()) { BeginReturn(false, now); return; }

        // the instant he starts dragging you out, the vanilla police take over: he never gets into your car
        bool beingPulled = false;
        try { beingPulled = Function.Call<bool>("IS_PED_BEING_JACKED", Game.LocalPlayer.Character); }
        catch (Exception) { }
        if (beingPulled && !arrestPullSeen) { arrestPullSeen = true; arrestPullAt = now; }
        if (arrestPullSeen && now - arrestPullAt > 6000)   // normally you are out long before this and the common checks start the arrest
        {
            BeginFootArrest(now);
            return;
        }

        // he has not got you out by himself: you are ordered out of the vehicle, and the vanilla arrest follows
        if (!arrestForced && !arrestPullSeen && now - arrestStart > ArrestForceOutMs)
        {
            arrestForced = true;
            Log("Officer did not pull you out, ordering you out of the vehicle");
            TryNative("TASK_LEAVE_CAR", Game.LocalPlayer.Character, playerVeh);
        }
        if (now - arrestStart > ArrestTimeoutMs) Handover("the officer could not get you out in time");
    }

    void EndPeacefully(int now)
    {
        if (Ok(copCar)) BeginReturn(true, now);
        else Finish(now);
    }

    // ----------------------------------------------------------------- back to the squad car

    void BeginReturn(bool peaceful, int now)
    {
        Log(peaceful ? "Officers walking back to their car" : "You drove off: officers running back to their car");
        peacefulReturn = peaceful;
        returnStart = now;
        putAwayPending = false;
        RemoveRadio();
        StopWriting(false);
        copTakeAt = 0;
        if (peaceful)
        {
            PoliceIgnore(false);
            Game.LocalPlayer.WantedLevel = 0;
        }
        else
        {
            StopPlayerAnim();
            afterAt = 0;
            // you drove off: from this instant you are an ordinary 1-star suspect to every cop in the game.
            // (Before, the police were still told to ignore you until the two officers were back in their
            // car, up to 22 seconds - no cop reacted to you and nothing you did raised your wanted level.)
            PoliceIgnore(false);
            HoldBackup(false);
            if (Game.LocalPlayer.WantedLevel < 1) Game.LocalPlayer.WantedLevel = 1;
        }
        foreach (Officer o in officers)
        {
            if (!Ok(o.Ped)) continue;
            o.Step = 10;
            o.StepAt = now;
            if (o.Ped.isInVehicle()) continue;
            if (peaceful) o.Ped.Task.ClearAll();
            else
            {
                // you drove off: whatever he was doing stops this instant
                try { o.Ped.Task.ClearAllImmediately(); }
                catch (Exception) { o.Ped.Task.ClearAll(); }
            }
            Vector3 door = copCar.GetOffsetPosition(new Vector3(o.IsDriver ? -1.7f : 1.7f, 0.2f, 0f));
            WalkTo(o.Ped, door, !peaceful);
        }
        stage = Stage.Return;
    }

    void TickReturn(int now)
    {
        bool allIn = true;
        foreach (Officer o in officers)
        {
            if (!Ok(o.Ped) || o.Ped.isInVehicle()) continue;
            allIn = false;
            VehicleSeat seat = o.IsDriver ? VehicleSeat.Driver : VehicleSeat.RightFront;
            Vector3 door = copCar.GetOffsetPosition(new Vector3(o.IsDriver ? -1.7f : 1.7f, 0.2f, 0f));

            if (o.Step == 10)
            {
                if (Dist2D(o.Ped.Position, door) < 2.2f || now - o.StepAt > 7000)
                {
                    o.Ped.Task.EnterVehicle(copCar, seat);
                    o.Step = 11;
                    o.StepAt = now;
                }
            }
            else if (o.Step == 11 && now - o.StepAt > 9000)
            {
                try { o.Ped.WarpIntoVehicle(copCar, seat); }
                catch (Exception) { }
            }
        }

        if (allIn || now - returnStart > 22000)
        {
            if (peacefulReturn) Finish(now);
            else Handover("you drove off, officers are back in their car");
        }
    }

    // ----------------------------------------------------------------- Pusher compatibility
    // Talks to Pusher.cs through AppDomain data. Without Pusher (or with its StopRightThereCompat off)
    // nothing here does anything and every stop is a normal ticket.
    static object PusherData(string key) { return AppDomain.CurrentDomain.GetData(key); }

    bool PusherLinked()
    {
        try
        {
            if (PusherData("Pusher.Version") == null) return false;
            if (!true.Equals(PusherData("Pusher.SrtLink"))) return false;
            object a = PusherData("Pusher.Alive");
            if (!(a is int)) return false;
            int age = Game.GameTime - (int)a;
            return age > -3000 && age < 3000;
        }
        catch (Exception) { return false; }
    }

    int PusherSearchState()
    {
        object v = PusherData("Pusher.SearchState");
        return v is int ? (int)v : 0;
    }

    // the officer is at your window: Pusher rolls whether this stop becomes a search
    bool TryPusherSearch(int now)
    {
        if (!PusherLinked() || officers.Count == 0 || !Ok(officers[0].Ped) || !Ok(playerVeh)) return false;
        try
        {
            Func<Ped, Ped, Vehicle, Vehicle, bool> f = PusherData("Pusher.BeginSearch2") as Func<Ped, Ped, Vehicle, Vehicle, bool>;
            if (f == null) return false;
            Ped partner = officers.Count > 1 && Ok(officers[1].Ped) ? officers[1].Ped : null;
            // the partner keeps his radio animation AND the radio in his hand; it is put away when the
            // animation ends or Pusher moves him (see TickPusherSearch)
            if (!f(officers[0].Ped, partner, playerVeh, copCar)) return false;
        }
        catch (Exception ex) { Log("Pusher search call failed: " + ex.Message); return false; }
        StopPlayerAnim();
        pusherSearchSince = now;
        stage = Stage.PusherSearch;
        Log("Pusher took the stop: " + (playerIsBike ? "frisk" : "trunk search"));
        return true;
    }

    void TickPusherSearch(int now)
    {
        Ped pp = Game.LocalPlayer.Character;
        if (!Ok(pp) || !pp.isAlive) { PoliceIgnore(false); Release(); return; }
        if (!PusherLinked()) { Handover("Pusher stopped during the search"); return; }
        if (now - pusherSearchSince > 600000) { Handover("search took too long"); return; }
        // while Pusher is still at the window (you have not answered yet) the partner is StopRightThere's as usual:
        // he walks up, does the radio (with the radio in his hand), then stands in his idle
        bool atWindow = false;
        try { object ph = PusherData("Pusher.SearchPhase"); atWindow = ph is int && (int)ph == 0; } catch (Exception) { }
        if (atWindow && PusherSearchState() == 1) TickPartner(now);
        // the radio goes away the moment his radio animation is over (or Pusher has him walking)
        if (!object.ReferenceEquals(radioProp, null))
        {
            bool talking = officers.Count > 1 && Ok(officers[1].Ped)
                && (now - officers[1].StepAt < 700 || AnimPlaying(officers[1].Ped, PartnerRadioSet, PartnerRadioAnim))
                && now - officers[1].StepAt < PartnerRadioMaxMs;
            if (!talking) RemoveRadio();
        }
        int st = PusherSearchState();
        if (st == 1) return;
        if (st == 2)
        {
            // nothing found and you are back in / on your vehicle: the officers go back to their spots
            // and the ticket goes on as normal (the reason line, accept / deny / drive off)
            Log("Pusher search: nothing found, back to the ticket");
            resumeNoRadio = true;
            if (Ok(playerVeh)) stopPos = playerVeh.Position;
            foreach (Officer o in officers)
            {
                if (!Ok(o.Ped)) continue;
                try { o.Ped.Task.ClearAll(); } catch (Exception) { }
                WalkTo(o.Ped, o.Post, false);
                o.Step = 1;
                o.StepAt = now;
            }
            stage = Stage.Approach;
            return;
        }
        if (st == 4) { Log("Pusher search: you drove off"); BeginReturn(false, now); return; }
        if (st == 3)
        {
            // Pusher keeps the officers for good (refused, arrested, the ride to the station):
            // they are let go of here without touching them
            Log("Pusher search: Pusher has the officers now");
            afterAt = 0;
            caughtAt = 0;
            RemoveRadio();
            StopWriting(false);
            officers.Clear();
            copCar = null;
            playerVeh = null;
            steering = false;
            gesturesBlocked = false;
            HoldBackup(false);
            stage = Stage.Idle;
            cooldownUntil = now + 30000;
            return;
        }
        Handover("Pusher search ended unexpectedly");
    }

    // ----------------------------------------------------------------- UI (same drawing method and layout as StrangerSystem)

    float aspect = 16.0f / 9.0f;
    int resW = 1920;
    int resH = 1080;

    class IconCmd
    {
        public Texture Tex;
        public float Aspect;
        public float Cx;
        public float Cy;
        public float H;
    }

    class TextTex
    {
        public Texture Tex;
        public float Aspect;
        public float CapCenter;
        public float PadRight;
    }

    List<IconCmd> iconCmds = new List<IconCmd>();
    List<IconCmd> pending = new List<IconCmd>();
    bool spritesBroken;
    Texture texDl, texDr;
    Dictionary<string, TextTex> textTex = new Dictionary<string, TextTex>();
    bool customFontOk;
    System.Drawing.Text.PrivateFontCollection fontCollection;

    void LoadIcons()
    {
        try
        {
            texDl = new Texture(Convert.FromBase64String(IconDl));
            texDr = new Texture(Convert.FromBase64String(IconDr));
            Log("Icon textures loaded");
        }
        catch (Exception ex)
        {
            texDl = null;
            texDr = null;
            Log("Icon load failed, using simple fallback icons: " + ex.Message);
        }
    }

    System.Drawing.FontStyle PickStyle(System.Drawing.FontFamily fam, bool wantBold)
    {
        if (wantBold && fam.IsStyleAvailable(System.Drawing.FontStyle.Bold)) return System.Drawing.FontStyle.Bold;
        if (fam.IsStyleAvailable(System.Drawing.FontStyle.Regular)) return System.Drawing.FontStyle.Regular;
        if (fam.IsStyleAvailable(System.Drawing.FontStyle.Bold)) return System.Drawing.FontStyle.Bold;
        return System.Drawing.FontStyle.Italic;
    }

    void LoadTextTextures()
    {
        try
        {
            if (!Directory.Exists(root)) return;

            string file = Path.Combine(root, FontFileName);
            if (FontFileName.Length == 0 || !File.Exists(file))
            {
                file = null;
                string[] all = Directory.GetFiles(root);
                Array.Sort(all);
                foreach (string f in all)
                {
                    string ext = Path.GetExtension(f).ToLower();
                    if (ext == ".ttf" || ext == ".otf") { file = f; break; }
                }
            }
            if (file == null)
            {
                Log("No .ttf/.otf found in " + root + "  (using the game's built-in font)");
                return;
            }

            fontCollection = new System.Drawing.Text.PrivateFontCollection();
            fontCollection.AddFontFile(file);
            if (fontCollection.Families.Length == 0)
            {
                Log("Font file could not be read: " + file);
                return;
            }
            System.Drawing.FontFamily fam = fontCollection.Families[0];
            System.Drawing.FontStyle regular = PickStyle(fam, false);
            System.Drawing.FontStyle bold = PickStyle(fam, true);

            textTex["Deny Ticket"] = RenderText(fam, regular, "Rechazar multa", 235, 235, 235, 255);
            textTex["Accept Ticket"] = RenderText(fam, regular, "Aceptar multa", 235, 235, 235, 255);
            textTex["Officer"] = RenderText(fam, bold, "Polic\u00eda", 255, 255, 255, 255);

            customFontOk = true;
            Log("Custom font loaded: " + fam.Name + " (" + Path.GetFileName(file) + ")");
        }
        catch (Exception ex)
        {
            customFontOk = false;
            textTex.Clear();
            Log("Custom font failed, using the built-in font: " + ex.Message);
        }
    }

    TextTex RenderText(System.Drawing.FontFamily fam, System.Drawing.FontStyle style, string text, int r, int g, int b, int a)
    {
        const float emPx = 64f;
        const int padX = 6;
        const int padY = 4;

        using (System.Drawing.Font font = new System.Drawing.Font(fam, emPx, style, System.Drawing.GraphicsUnit.Pixel))
        using (System.Drawing.StringFormat fmt = new System.Drawing.StringFormat(System.Drawing.StringFormat.GenericTypographic))
        {
            int lineH = (int)Math.Ceiling(font.GetHeight());

            float textW;
            using (System.Drawing.Bitmap tmp = new System.Drawing.Bitmap(1, 1))
            using (System.Drawing.Graphics mg = System.Drawing.Graphics.FromImage(tmp))
            {
                textW = mg.MeasureString(text, font, 4096, fmt).Width;
            }

            int bmpW = (int)Math.Ceiling(textW) + padX * 2;
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
                        for (int col = 0; col < 160; col++)
                        {
                            if (hb.GetPixel(col, row).A > 128) { hit = true; break; }
                        }
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

    bool AddTextSprite(string key, float right, float cy, float heightNorm)
    {
        TextTex t;
        if (!customFontOk || spritesBroken || !textTex.TryGetValue(key, out t)) return false;

        float xScale = (float)resH / (float)resW;
        float wNorm = heightNorm * t.Aspect * xScale;
        IconCmd c = new IconCmd();
        c.Tex = t.Tex;
        c.Aspect = t.Aspect;
        c.H = heightNorm;
        c.Cx = right - wNorm * 0.5f + t.PadRight * heightNorm * xScale;
        c.Cy = cy + (0.5f - t.CapCenter) * heightNorm + TextYOffset;
        pending.Add(c);
        return true;
    }

    void UpdateResolution()
    {
        try
        {
            RECT r;
            if (GetClientRect(GetForegroundWindow(), out r))
            {
                int w = r.Right - r.Left;
                int h = r.Bottom - r.Top;
                if (w > 100 && h > 100)
                {
                    resW = w;
                    resH = h;
                    aspect = (float)w / (float)h;
                }
            }
        }
        catch (Exception) { }
    }

    void Rect(float cx, float cy, float w, float h, int r, int g, int b, int a)
    {
        Function.Call("DRAW_RECT", cx, cy, w, h, r, g, b, a);
    }

    int SafeFont(int f)
    {
        if (f == 0 || f == 1 || f == 3 || f == 4 || f == 5) return f;
        return 0;
    }

    void DrawTextF(int font, string s, float x, float y, float scale, int r, int g, int b, int a, bool rightAlign, bool centre)
    {
        Function.Call("SET_TEXT_FONT", SafeFont(font));
        Function.Call("SET_TEXT_SCALE", scale, scale);
        Function.Call("SET_TEXT_COLOUR", r, g, b, a);
        Function.Call("SET_TEXT_DROPSHADOW", 1, 0, 0, 0, 255);
        if (rightAlign)
        {
            Function.Call("SET_TEXT_RIGHT_JUSTIFY", 1);
            Function.Call("SET_TEXT_WRAP", 0.0f, x);
        }
        if (centre)
        {
            Function.Call("SET_TEXT_CENTRE", 1);
        }
        Function.Call("DISPLAY_TEXT_WITH_LITERAL_STRING", x, y, "STRING", s);
    }

    void DrawText(string s, float x, float y, float scale, int r, int g, int b, int a, bool rightAlign, bool centre)
    {
        DrawTextF(HudFont, s, x, y, scale, r, g, b, a, rightAlign, centre);
    }

    void DrawDpad(float cx, float cy, float h, bool leftArm, bool enabled)
    {
        float w = h / aspect;
        float t = h / 3.0f;
        float tw = w / 3.0f;
        int body = enabled ? 95 : 55;
        int hi = enabled ? 255 : 120;
        Rect(cx, cy, w, t, body, body, body, 235);
        Rect(cx, cy, tw, h, body, body, body, 235);
        if (leftArm) Rect(cx - tw, cy, tw, t, hi, hi, hi, 255);
        else Rect(cx + tw, cy, tw, t, hi, hi, hi, 255);
    }

    const float DpadAspect = 91.0f / 52.0f;

    float DpadHeight()
    {
        return 0.026f * IconScale;
    }

    // width of the icon column - the same as StrangerSystem's (sized for its widest icon)
    float ColWidth()
    {
        return 0.026f * IconScale * (91.0f / 52.0f) * (float)resH / (float)resW;
    }

    void AddIcon(bool leftArm, float cx, float cy)
    {
        Texture t = leftArm ? texDl : texDr;
        if (t != null && !spritesBroken)
        {
            IconCmd c = new IconCmd();
            c.Tex = t;
            c.Aspect = DpadAspect;
            c.Cx = cx;
            c.Cy = cy;
            c.H = DpadHeight();
            pending.Add(c);
            return;
        }
        DrawDpad(cx, cy, DpadHeight(), leftArm, true);
    }

    void DrawRow(float rightEdge, float cy, string label, bool leftArm)
    {
        float colW = ColWidth();
        float iconCx = rightEdge - colW * 0.5f;
        float textRight = rightEdge - colW - 0.008f;

        if (!AddTextSprite(label, textRight, cy, OptionTextHeight))
            DrawText(label == "Deny Ticket" ? "Rechazar multa" : (label == "Accept Ticket" ? "Aceptar multa" : label), textRight, cy - 0.0717f * OptionScale + TextYOffset, OptionScale, 235, 235, 235, 255, true, false);
        AddIcon(leftArm, iconCx, cy);
    }

    void DrawHud()
    {
        // two option rows sitting where StrangerSystem's 2nd and 3rd rows are, so the divider and name land in the same place
        float y = RightTop + RowStep;
        DrawRow(RightEdge, y, "Deny Ticket", true);
        y += RowStep;
        DrawRow(RightEdge, y, "Accept Ticket", false);

        float divY = y + RowStep * 0.62f;
        Rect(RightEdge - 0.075f, divY, 0.15f, 0.0016f, 255, 255, 255, 190);

        float nameY = y + RowStep * 1.22f;
        if (!AddTextSprite("Officer", RightEdge, nameY, NameTextHeight))
            DrawText("Polic\u00eda", RightEdge, nameY - 0.0717f * NameScale + TextYOffset, NameScale, 255, 255, 255, 255, true, false);
    }

    void OnDraw(object sender, GraphicsEventArgs e)
    {
        if (spritesBroken) return;
        List<IconCmd> cmds = iconCmds;
        if (cmds.Count == 0) return;
        try
        {
            foreach (IconCmd c in cmds)
            {
                float h = c.H * (float)resH;
                float w = h * c.Aspect;
                float x = c.Cx * (float)resW - w * 0.5f;
                float y = c.Cy * (float)resH - h * 0.5f;
                if (SpriteOriginIsCenter) { x += w * 0.5f; y += h * 0.5f; }
                e.Graphics.DrawSprite(c.Tex, x, y, w, h, 0.0f);
            }
        }
        catch (Exception ex)
        {
            spritesBroken = true;
            Log("DrawSprite failed, switching to fallback icons: " + ex.Message);
        }
    }
}
