# Stick Buddy

A red stick figure who lives on your desktop and is your voice assistant.
He has his own little house, walks and does parkour around your screen, talks, has feelings,
plays games, and does things on your PC.

## Get it running (Windows 10 / 11)
1. Download this project (green **Code** button > **Download ZIP**) and unzip it.
2. Double-click **build.bat**. It uses the C# compiler that already comes with Windows,
   so you don't need to install anything. It makes **StickBuddy.exe**.
3. Double-click **StickBuddy.exe**.

## Use your own AI key (optional)
Stick Buddy works without any key. To give him his smart brain, right-click him > **Settings**
and paste **your own** key into **API key**:
- a free **Gemini** key from Google AI Studio: https://aistudio.google.com/apikey
- or a **Claude** key (starts with `sk-ant-`) from https://console.anthropic.com

Your key stays on your own computer: it's saved encrypted in `%APPDATA%\StickBuddy\settings.ini`
(locked to your Windows account) and is never part of this project. You can also set the
`GEMINI_API_KEY` or `ANTHROPIC_API_KEY` environment variable instead.
Never paste your key into an issue, a chat or a screenshot.

## Start him
Double-click **StickBuddy.exe**. His house appears at the bottom of the screen and he's
already inside doing something: sleeping, watching TV, drawing, exercising or playing basketball.
If he's asleep, pick him up and drop him (or smash him on the ground!) to wake him up.

To start him every time Windows starts: press `Win + R`, type `shell:startup`, press Enter,
and put a shortcut to StickBuddy.exe in that folder.

## Talk to him
- Say his name first: **"Sticky, open chrome"**, or say **"Sticky"**, wait for "Yes?", then talk.
- Or type: **double-click him**, or press **Ctrl + Shift + Space**.
- **Ctrl + Shift + L** = he listens without you saying his name.
- **Right-click him** (or the tray icon) for the menu: games, his house, settings, help, exit.

## Things to try
| Say | What happens |
|---|---|
| open chrome / open roblox / open calculator | he runs to Start, stomps it, types the name, and opens the app |
| what is 1 + 1 / what's 12 times 7 | he answers, then climbs on Notepad and scribbles the answer in |
| what animal lived the longest / capital of France | same, for facts he knows |
| write down buy milk | he writes it in Notepad |
| search for cute cats / play lofi on youtube | opens your browser |
| play tag / chase me / I'm it / hide and seek | games! (see below) |
| tic tac toe / snake / rock paper scissors | more games (he plays Snake by himself) |
| go home / watch tv / draw a picture / play basketball / exercise / clean up | he goes into his house |
| dance / do a flip / jump / wave / sit down / go to sleep / come here | moves |
| turn blue / turn rainbow / get bigger / change your name to Bob | customise him |
| volume up / next song / pause music / take a screenshot | PC stuff |
| tell me a joke / how are you | chat |

## Grab him, throw him, smash him
- **Drag him** with the mouse: he dangles and swings like a real ragdoll and kicks his legs.
- **Throw him**: he flies, crashes, lies there dizzy, then gets back up.
- **Smash him into the edge of the screen**: the whole screen flashes black with him in white
  for a split second, and the edge of the screen cracks. (You can switch the flash off in Settings.)
- He can **land on windows**. Make a window smaller (not minimised) and he may jump on top of it,
  or throw him onto it.

## Game Center
Say **"games"** (or right-click him > **Game Center**) to see all his games with pictures and your best scores.
New arcade games:
- **Balloon Pop**: balloons float around your screen. Grab Sticky and *throw him* into them!
  Gold balloons are worth 3, and popping several in one throw gives a combo. 45 seconds.
- **Dodgeball**: he throws balls at your mouse pointer and you dodge by moving the mouse.
  3 lives, 40 seconds, and he throws faster and faster.
- **Whack-a-Sticky**: he pops up from behind your windows, his roof and the bottom of the screen.
  Click him before he ducks! Golden Sticky is worth 3.
- **Hoops**: a basketball shootout at his house. You throw the ball into his hoop with the mouse,
  then he takes his shot. 5 shots each (no dunking!).
- **Dance Battle**: a rhythm game with its own music. Press the arrow keys (or W A S D) when the
  arrows reach the top. Sticky dances on top of the window; miss and he stumbles. Easy, normal or hard.
- **Guess My Drawing**: he draws something bit by bit and you type (or say) what it is.
  The faster you guess, the more points. 5 rounds, with a hint halfway.

During games he doesn't get angry about being thrown around, and there's no black flash.
Best scores are saved in `%APPDATA%\StickBuddy\scores.txt`.

## Tag (with parkour)
- **"I'm it"** (or menu > Tag - you're it): he runs away from your mouse. Click him 5 times in 60 seconds.
  He slides under your mouse, jumps over it, climbs walls and jumps onto windows. It's hard!
- **"Chase me"** (or menu > Tag - I'm it): he chases your mouse with parkour and dives at it.
  One tag and he wins. Survive 45 seconds and you win.

## Hide and seek
Say **"hide and seek"**. The screen goes dark and counts down from 10 while he hides behind one of
your files in File Explorer (in Downloads, Documents, Pictures, Videos, Music or Desktop).
Then find him: open those folders and look closely, you might have to scroll! When you see a
bit of him peeking out from behind a file, click him. Say **"I give up"** if you can't find him.
He only looks at file names; he never opens, moves or changes your files.

## His house
- He goes home when you leave him alone for a while: he watches TV, draws a random picture on
  his easel (and hangs it on the wall), plays basketball outside, exercises, or naps in his bed.
- **Drag his things** (TV, lamp, plant, ball, dumbbell, chair, picture, teddy) out of the house
  and throw them around. Throw them hard and they **break** (he won't be happy...).
  Broken things come back after a while, and he tidies up things you threw around.
- Drag the house by its walls to move it. "hide your house" / "show your house" to put it away.

## Feelings
- **Bored**: too many questions in a row and he sighs and sometimes asks to play a game instead.
  Leave him alone and he goes home to play.
- **Happy**: games, jokes and compliments make him happy and bouncy.
- **Angry**: keep grabbing and smashing him (or be mean) and he gets angrier, step by step:
  1. he warns you ("Stop it! I mean it!")
  2. he grabs your cursor and **throws it away**, or **hangs on it** so it gets heavy and sinks down
  3. he jumps on your cursor and **stomps it around** the screen. Wiggle your mouse hard to shake
     him off... but he'll come right back!
  
  Press **Esc** any time to stop him, or say **"sorry"**.
- **Watching you**: when you watch YouTube he sits down with popcorn and watches too
  (speech bubbles only, so he doesn't talk over your video). When you download something
  he says something about it. Both can be switched off in Settings.

## He plays your game for you (needs a smart-brain key)
Open a game, click on it so it's in front, then say **"play this game for me"**
(or "play roblox for me and collect coins", or right-click him > **Play the game I'm in**).
He sits on the game window with a controller and plays by himself:
1. he takes a picture of **only the game window**,
2. his brain (Gemini or Claude) looks at it and decides what to do,
3. he presses the keys and clicks the mouse, then looks again.

He plays any game you have open, and gets better at it:
- **He keeps moving while he thinks**: he keeps holding keys (like W to run) between looks and steers,
  instead of stopping every time.
- **He knows some games**: Roblox (turns the camera by dragging with the right mouse button,
  knows about obbies and kill bricks), Minecraft, and games in a web browser (only clicks inside the game).
- **He learns**: after each game he writes himself a few tips (in `%APPDATA%\StickBuddy\games\`)
  and reads them the next time he plays that game.
- Say **"play roblox for me for 10 minutes"** to let him play longer (up to 30 minutes).
  If Roblox isn't open yet he opens it; pick a game, click on it, and ask him again.

To stop him: press **Esc**, say **"stop"**, move your mouse, or click another window.
He stops by himself after 5 minutes (or the time you said), or when he sees a shop, purchase, login or chat screen.

Safety: he can only press game keys (W A S D, arrows, Space, Shift, E F Q R Z X C, 1-9) and
click inside the game window. He can never press Enter or type, so he can't chat.
Still, stay with him while he plays, and don't let him play games where one click can spend money.

Honest limits: every look-and-think takes a second or two, so he plays in little bursts.
He's fine for slow games (puzzle, clicker, turn-based, easy browser games) and clumsy in fast ones.
Each turn sends a picture of the game window to Google or Anthropic, and uses up a bit of your
key's free limit; if it runs out he takes a breather and tries again.

## Obby Lab: teach him to play
Say **"obby"** (or right-click him > **Obby Lab**). Build an obstacle course with blocks, lava,
bounce pads, checkpoints, a start flag and a finish flag (left-click to place, right-click to erase),
or pick one from **Examples**. Then press **Teach him!**

40 little copies of him try the obby at once. Each one has a real neural network as a brain:
it sees the tiles around him and decides when to press left, right and jump. The ones who get
furthest become the parents of the next generation, with small random changes (that's evolution,
a real kind of machine learning). Use the speed buttons (5x, 20x, MAX) for hard obbies.
The panel on the right shows his IQ, how far he gets each generation, and his brain thinking live.

His brain grows up:
- **IQ 0**: baby brain. He has no idea what he's doing and babbles ("ba ba! goo?").
- **IQ 20**: he talks in single words ("hot!", "jump!").
- **IQ 50**: he learns **parkour**: wall slides and wall jumps.
- **IQ 90**: he talks in full sentences (with a smart-brain key he comes up with his own lines).

Every generation makes him a bit smarter, and beating a new obby gives a big boost.
What he learned carries over to the next obby. "Reset his brain" starts him at IQ 0 again.
Ask him **"what's your IQ?"** any time.

## Make him smart (optional)
Without a key he can do math, open apps, play games and answer some built-in questions.
For anything else he searches the internet for you.

Right-click him > Settings, and paste a key into **API key**:
- a **Gemini key** from Google AI Studio (aistudio.google.com/apikey), or
- a **Claude key** (starts with `sk-ant-`, from console.anthropic.com).

He notices when you paste it ("Is that a BRAIN?!"), and when you press Save he wakes up
and says his first smart sentence. With a key he:
- talks about almost anything, with his own personality and feelings,
- **remembers** things you tell him about yourself (stored in `%APPDATA%\StickBuddy\memories.txt`;
  say "forget everything" to wipe it),
- knows what just happened to him (being thrown, broken TV, games won and lost, the obby he learned),
- sometimes **says something by himself** when you've been quiet for a while (you can switch this off).

What you say to him, his mood, the name of the app you're using, and the title of a YouTube video
he's watching with you are sent to Google or Anthropic to make his answers. Never your files or other window titles.
The key is stored encrypted with your Windows account. Treat it like a password and don't share it.

## Change the code
The code is in the `src` folder (C#). Double-click **build.bat** to rebuild StickBuddy.exe;
it uses the C# compiler that comes with Windows, so nothing needs installing.
Settings and a log are in `%APPDATA%\StickBuddy`.

- `Figure.cs` - all his poses and dances, and how he's drawn
- `Ragdoll.cs` - the floppy physics when you hold or throw him
- `Buddy.cs` - the main loop, physics, dragging, drawing
- `House.cs`, `Things.cs` - his house, his things and his drawings
- `Life.cs` - what he does at home and when he's bored
- `Parkour.cs` - parkour moves and tag
- `Anger.cs` - the anger tiers and mouse tricks
- `HideSeek.cs` - hide and seek in File Explorer
- `Watch.cs` - watching YouTube and noticing downloads
- `Brain.cs` - understanding what you say, and his feelings
- `Mind.cs` - his smart brain: memory, recent events, talking by himself
- `Claude.cs`, `Gemini.cs` - talking to Claude or Gemini over the internet
- `Learner.cs` - the obby physics, his neural network and the evolution that trains it
- `ObbyLab.cs` - the Obby Lab window

Test modes: `--lab-test out.txt` (a fresh brain learns each example obby, no window),
`--lab-render out.png` (pictures of the lab), `--gemini-test out.txt` (checks the connection with a fake key).

`StickBuddy.exe --render-test picture.png` draws every pose, the ragdoll, the house and some
of his drawings into one picture.
