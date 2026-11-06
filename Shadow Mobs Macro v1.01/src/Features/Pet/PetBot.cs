using System;
using System.Drawing;
using System.IO;

namespace StaminaDonator
{
    // Auto-train pet: every 20 minutes, Safehouse > ANIMALS > ANIMAL TRAINING > TRAIN, then closes the
    // Animals window. If the pet is still resting it reads "Train again in 19:58" and comes back then.
    // The next training time is kept in pet-next.txt, so a restart knows it.
    // Feeding (if ticked) happens first, on the OWNED tab the window opens on: see FeedIfHungry.
    // The only things it clicks are the SAFEHOUSE menu button, ANIMALS, the OWNED and ANIMAL TRAINING
    // tabs, FEED 10% / FEED 50%, TRAIN and the window's X. TRAIN only once the window and its training
    // tab have both been read; a FEED button only once it's been read to say FEED and its number.
    public class PetBot
    {
        public const int EnergyCost = 10;              // a training session's energy cost (shown on the tab)
        static readonly TimeSpan Rest = TimeSpan.FromMinutes(20);

        readonly IGame game;
        readonly PetVision vision;
        readonly Settings settings;
        readonly Random rng = new Random();
        bool feedWarned;
        DateTime premiumOutUntil = DateTime.MinValue, foodOutUntil = DateTime.MinValue;   // after a feed that did nothing

        public Action<string> Status = delegate { };
        public DateTime NextTrain = DateTime.MinValue;   // MinValue: unknown, so go and look
        public int Trained, Fed;

        public PetBot(IGame game, PetVision vision, Settings settings)
        {
            this.game = game;
            this.vision = vision;
            this.settings = settings;
            NextTrain = LoadNext();
        }

        public bool Due(DateTime now) { return now >= NextTrain; }

        // One visit: feed if it's ticked and the pet is hungry (OWNED tab), get to the training tab, train
        // if it's ready (otherwise read how long it's resting), then close the window. Always sets NextTrain.
        public void TrainOnce()
        {
            Status("Training the pet");
            bool wantFeed = settings.PetFeed10 || settings.PetFeed50;
            if (wantFeed && !vision.CanFeed && !feedWarned)
            {
                feedWarned = true;
                Log.Write("Feeding is ticked, but the pet calibration is from before feeding: press CALIBRATE and redo Auto-Train Pet");
            }
            bool feedChecked = !wantFeed || !vision.CanFeed;
            int ownedClicks = 0;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                Frame f = game.Capture();
                if (f == null) { game.Sleep(1000); continue; }
                if (vision.WindowOpen(f))
                {
                    if (!feedChecked)
                    {
                        if (vision.OwnedShowing(f)) { FeedIfHungry(f); feedChecked = true; continue; }
                        if (ownedClicks++ < 2) { Log.Write("Trying to click OWNED"); Click(vision.Owned, 5); game.Sleep(900); continue; }
                        Log.Write("Couldn't get to the OWNED tab to check the hunger; training without feeding");
                        feedChecked = true;
                    }
                    if (!PetVision.IsTrainingPanel(vision.Text(f, vision.PanelArea)))
                    {
                        Log.Write("Trying to click ANIMAL TRAINING");
                        Click(vision.Tab, 5);
                        game.Sleep(900);
                        continue;
                    }
                    // Move off the button before judging its colour, then look again.
                    game.MoveMouse(vision.Park.X, vision.Park.Y);
                    game.Sleep(300);
                    Frame g = game.Capture();
                    if (g == null) continue;
                    string panel = vision.Text(g, vision.PanelArea);
                    if (vision.Lit(g)) Train();
                    else Resting(panel);
                    CloseWindow();
                    return;
                }
                if (vision.AnimalsShowing(f))
                {
                    Log.Write("Trying to click ANIMALS");
                    Click(vision.AnimalsButton, 5);
                    game.Sleep(1200);
                    continue;
                }
                Log.Write("Trying to click SAFEHOUSE");
                Click(vision.SafehouseButton, 6);
                game.Sleep(1200);
            }
            SetNext(game.Now.AddMinutes(2));
            Log.Write("Couldn't get to Animal Training; trying again in 2 minutes");
        }

        // On the OWNED tab: feeds while a portion still fits, premium food first when both are ticked
        // (FEED 50% at 50% hunger or less, FEED 10% at 90% or less). A count of 0 means none; a count
        // that can't be read (a lone "1" often can't) is tried anyway. Each feed must raise the hunger;
        // if it doesn't, that food isn't tried again for an hour (unless its count then reads above 0).
        void FeedIfHungry(Frame f)
        {
            bool premiumFailed = false, foodFailed = false;
            for (int feeds = 0; feeds < 12; feeds++)
            {
                int hunger = vision.ReadHunger(f);
                if (hunger < 0) { Log.Write("Couldn't read the pet's hunger; not feeding this time"); return; }
                int food, premium;
                vision.ReadFood(f, out food, out premium);
                DateTime now = game.Now;
                bool premiumOk = settings.PetFeed50 && !premiumFailed && premium != 0 && (premium > 0 || now >= premiumOutUntil);
                bool foodOk = settings.PetFeed10 && !foodFailed && food != 0 && (food > 0 || now >= foodOutUntil);
                bool big = premiumOk && hunger <= 50, small = !big && foodOk && hunger <= 90;
                if (!big && !small)
                {
                    string note = "";
                    if (settings.PetFeed50 && hunger <= 50 && premium == 0) note += ", no premium food left";
                    if (settings.PetFeed10 && hunger <= 90 && food == 0) note += ", no food left";
                    if (feeds == 0 || note.Length > 0) Log.Write("Pet hunger " + hunger + "%" + note);
                    return;
                }
                string name = big ? "FEED 50%" : "FEED 10%";
                Point at;
                if (!vision.FindFeed(f, big ? vision.Feed50 : vision.Feed10, out at))
                {
                    Log.Write("Couldn't read " + name + " where the calibration says; not feeding (calibrate the pet again?)");
                    return;
                }
                Log.Write(string.Format("Hunger {0}%: trying to click {1}", hunger, name));
                game.Click(at.X + rng.Next(-4, 5), at.Y + rng.Next(-2, 3));
                game.MoveMouse(vision.FeedPark.X, vision.FeedPark.Y);
                game.Sleep(1200);
                Frame g = game.Capture();
                if (g == null) return;
                int after = vision.ReadHunger(g);
                if (after < 0) { Log.Write("Couldn't read the hunger after feeding; no more feeding this time"); return; }
                if (after > hunger)
                {
                    Fed++;
                    Log.Write(string.Format("Fed the pet: hunger {0}% -> {1}%", hunger, after));
                }
                else
                {
                    int count = big ? premium : food;
                    Log.Write(name + " didn't raise the hunger" + (count > 0 ? "; trying again next visit"
                        : big ? " (no premium food left?); not trying it for an hour" : " (no food left?); not trying it for an hour"));
                    if (big) { premiumFailed = true; premiumOutUntil = now.AddHours(1); }
                    else { foodFailed = true; foodOutUntil = now.AddHours(1); }
                }
                f = g;
            }
        }

        // Presses TRAIN (up to 3 times) until it turns into RESTING.
        void Train()
        {
            for (int press = 0; press < 3; press++)
            {
                Log.Write("Trying to click TRAIN");
                Click(vision.Train, 5);
                game.MoveMouse(vision.Park.X, vision.Park.Y);
                game.Sleep(1500);
                Frame after = game.Capture();
                if (after == null || vision.Lit(after)) continue;
                Trained++;
                TimeSpan? left = PetVision.Timer(vision.Text(after, vision.PanelArea));
                SetNext(game.Now + (left ?? Rest) + TimeSpan.FromSeconds(3));
                Log.Write("Trained the pet; next training at " + NextTrain.ToString("HH:mm:ss"));
                return;
            }
            SetNext(game.Now.AddMinutes(1));
            Log.Write("Pressed TRAIN 3 times but it didn't take; trying again in a minute");
        }

        // Not lit: resting (with a timer), or it can't train for another reason (energy, hunger).
        void Resting(string panel)
        {
            TimeSpan? left = PetVision.Timer(panel);
            if (left != null)
            {
                SetNext(game.Now + left.Value + TimeSpan.FromSeconds(3));
                Log.Write("The pet is resting; next training at " + NextTrain.ToString("HH:mm:ss"));
            }
            else
            {
                SetNext(game.Now.AddMinutes(5));
                Log.Write("TRAIN isn't lit and there's no timer (not enough energy, or the pet is hungry?); looking again in 5 minutes");
            }
        }

        // Clicks the X until the window has gone (up to 3 tries).
        void CloseWindow()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                Click(vision.Close, 4);
                game.Sleep(700);
                Frame f = game.Capture();
                if (f == null || !vision.WindowOpen(f)) return;
            }
            Log.Write("The Animals window didn't close");
        }

        void Click(Rectangle b, int spread)
        {
            game.Click(b.X + b.Width / 2 + rng.Next(-b.Width / spread, b.Width / spread + 1),
                       b.Y + b.Height / 2 + rng.Next(-b.Height / spread, b.Height / spread + 1));
        }

        static string FilePath { get { return App.PathOf("pet-next.txt"); } }

        void SetNext(DateTime when)
        {
            NextTrain = when;
            try { File.WriteAllText(FilePath, when.ToString("o")); }
            catch (IOException) { }
        }

        static DateTime LoadNext()
        {
            try
            {
                DateTime t;
                if (File.Exists(FilePath) && DateTime.TryParse(File.ReadAllText(FilePath).Trim(), null, System.Globalization.DateTimeStyles.RoundtripKind, out t)) return t;
            }
            catch (IOException) { }
            return DateTime.MinValue;
        }
    }
}
