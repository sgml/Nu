﻿namespace BlazeVector
open System
open Prime
open Nu
open BlazeVector

[<AutoOpen>]
module BlazeDispatcherModule =

    /// The custom type for BlazeVector's game dispatcher.
    type BlazeDispatcher () =
        inherit GameDispatcher ()

        // this function handles the selection of the title screen by playing the song "Machinery"
        static let handleSelectTitleScreen _ world =
            World.playSong 0 1.0f Assets.MachinerySong world

        // this function handles the clicking of the play button on the title screen by playing
        // the game
        static let handleClickTitlePlay _ world =
            let world = World.fadeOutSong Constants.Audio.DefaultTimeToFadeOutSongMs world
            World.transitionScreen Simulants.Gameplay world

        // this function creates the BlazeVector title screen to the world
        static let createTitleScreen world =

            // this creates a dissolve screen from the specified file with the given parameters
            let world = World.createDissolveScreenFromLayerFile (Some Simulants.Title.ScreenName) Constants.BlazeVector.DissolveData Assets.TitleLayerFilePath world |> snd

            // this subscribes to the event that is raised when the Title screen is selected for
            // display and interaction, and handles the event by playing the song "Machinery".
            //
            // You will need to familiarize yourself with the calling conventions of the various
            // World.monitor functions as well as the event address operators '->-' and its ilk
            // by studying their types and documentation comments.
            let world = World.monitor handleSelectTitleScreen (Events.Select ->- Simulants.Title) Simulants.Game world

            // subscribes to the event that is raised when the Title screen's Play button is
            // clicked, and handles the event by transitioning to the Gameplay screen
            let world = World.monitor handleClickTitlePlay (Events.Click ->- Simulants.TitlePlay) Simulants.Game world

            // subscribes to the event that is raised when the Title screen's Credits button is
            // clicked, and handles the event by transitioning to the Credits screen
            let world = World.monitor (World.handleAsScreenTransition Simulants.Credits) (Events.Click ->- Simulants.TitleCredits) Simulants.Game world

            // subscribes to the event that is raised when the Title screen's Exit button is clicked,
            // and handles the event by exiting the game
            World.monitorPlus World.handleAsExit (Events.Click ->- Simulants.TitleExit) Simulants.Game world |> snd

        // pretty much the same as above, but for the Credits screen
        static let createCreditsScreen world =
            let world = World.createDissolveScreenFromLayerFile (Some Simulants.Credits.ScreenName) Constants.BlazeVector.DissolveData Assets.CreditsLayerFilePath world |> snd
            World.monitor (World.handleAsScreenTransition Simulants.Title) (Events.Click ->- Simulants.CreditsBack) Simulants.Game world

        // and so on.
        static let createGameplayScreen world =
            let world = World.createDissolveScreenFromLayerFile<GameplayScreenDispatcher> (Some Simulants.Gameplay.ScreenName) Constants.BlazeVector.DissolveData Assets.GameplayLayerFilePath world |> snd
            World.monitor (World.handleAsScreenTransition Simulants.Title) (Events.Click ->- Simulants.GameplayBack) Simulants.Game world

        // game registration is where the game's high-level logic is set up!
        override dispatcher.Register (_, world) =

            // hint to the renderer and audio system that the 'Gui' package should be loaded up front
            let world = World.hintRenderPackageUse Assets.GuiPackageName world
            let world = World.hintAudioPackageUse Assets.GuiPackageName world

            // create our screens
            let world = createTitleScreen world
            let world = createCreditsScreen world
            let world = createGameplayScreen world

            // create a splash screen that automatically transitions to the Title screen
            let (splash, world) = World.createSplashScreen (Some Simulants.Splash.ScreenName) Constants.BlazeVector.SplashData Simulants.Title world

            // play a neat sound effect, select the splash screen, and we're off!
            let world = World.playSound 1.0f Assets.NuSplashSound world
            World.selectScreen splash world