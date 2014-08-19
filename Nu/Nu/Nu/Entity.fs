﻿namespace Nu
open System
open System.Collections.Generic
open System.ComponentModel
open System.Reflection
open System.Xml
open System.Xml.Serialization
open OpenTK
open TiledSharp
open Prime
open Nu
open Nu.NuConstants

[<AutoOpen>]
module EntityModule =

    type Entity with

        static member init (entity : Entity) (dispatcherContainer : IXDispatcherContainer) : Entity = entity?Init (entity, dispatcherContainer)
        static member register (address : Address) (entity : Entity) (world : World) : World = entity?Register (address, world)
        static member unregister (address : Address) (entity : Entity) (world : World) : World = entity?Unregister (address, world)
        static member usesFacet (facetName : string) (entity : Entity) (world : World) : bool = entity?UsesFacet (facetName, world)

    type EntityDispatcher (facetNames : Set<string>) =

        new () = EntityDispatcher (Set.empty)

        abstract member Init : Entity * IXDispatcherContainer -> Entity
        default dispatcher.Init (entity, _) = entity

        abstract member Register : Address * World -> World
        default dispatcher.Register (_, world) = world

        abstract member Unregister : Address * World -> World
        default dispatcher.Unregister (_, world) = world

        member dispatcher.UsesFacet (facetName, _ : World) =
            Set.contains facetName facetNames

    type [<StructuralEquality; NoComparison>] TileMapData =
        { Map : TmxMap
          MapSize : int * int
          TileSize : int * int
          TileSizeF : Vector2
          TileMapSize : int * int
          TileMapSizeF : Vector2
          TileSet : TmxTileset
          TileSetSize : int * int }

    type [<StructuralEquality; NoComparison>] TileData =
        { Tile : TmxLayerTile
          I : int
          J : int
          Gid : int
          GidPosition : int
          Gid2 : int * int
          OptTileSetTile : TmxTilesetTile option
          TilePosition : int * int }

    type Entity with
    
        static member makeDefaultUninitialized dispatcherName optName =
            let id = NuCore.makeId ()
            { Id = id
              Name = match optName with None -> string id | Some name -> name
              Visible = true
              OptOverlayName = Some dispatcherName
              Xtension = { XFields = Map.empty; OptXDispatcherName = Some dispatcherName; CanDefault = true; Sealed = false }}

        static member makeDefault dispatcherName optName dispatcherContainer =
            let entity = Entity.makeDefaultUninitialized dispatcherName optName
            Entity.init entity dispatcherContainer

        static member shouldWriteProperty overlayer entity propertyName =
            match entity.OptOverlayName with
            | None -> true
            | Some overlayName ->
                match Overlayer.trySelectNode overlayName propertyName overlayer with
                | None -> true
                | Some overlayNode ->
                    let propertyValue =
                        match (entity.GetType ()).GetProperty (propertyName, BindingFlags.Public ||| BindingFlags.Instance) with
                        | null -> Map.find propertyName entity.Xtension.XFields
                        | entityProperty -> entityProperty.GetValue entity
                    let converter = TypeDescriptor.GetConverter <| propertyValue.GetType ()
                    if converter.CanConvertFrom typeof<string> then
                        let overlayValue = converter.ConvertFrom overlayNode.InnerText
                        overlayValue <> propertyValue
                    else true

        static member writeToXml overlayer (writer : XmlWriter) entity =
            writer.WriteStartElement typeof<Entity>.Name
            Xtension.writeTargetProperties (Entity.shouldWriteProperty overlayer entity) writer entity
            writer.WriteEndElement ()

        static member writeManyToXml overlayer writer (entities : Map<_, _>) =
            for entityKvp in entities do
                Entity.writeToXml overlayer writer entityKvp.Value

        static member readFromXml (entityNode : XmlNode) defaultDispatcherName world =
            let entity = Entity.makeDefaultUninitialized defaultDispatcherName None
            Xtension.readTargetXDispatcher entityNode entity
            let entity = { entity with OptOverlayName = entity.Xtension.OptXDispatcherName }
            let entity = Entity.init entity world
            Xtension.readTargetProperties entityNode entity
            match entity.OptOverlayName with
            | None -> ()
            | Some overlayName -> Overlayer.applyOverlay overlayName entity world.Overlayer
            entity

        static member readManyFromXml (parentNode : XmlNode) defaultDispatcherName world =
            let entityNodes = parentNode.SelectNodes "Entity"
            let entities =
                Seq.map
                    (fun entityNode -> Entity.readFromXml entityNode defaultDispatcherName world)
                    (enumerable entityNodes)
            Seq.toList entities

[<AutoOpen>]
module WorldEntityModule =

    type World with

        static member private optEntityFinder (address : Address) world =
            let optGroupMap = Map.tryFind (List.at 0 address) world.Entities
            match optGroupMap with
            | None -> None
            | Some groupMap ->
                let optEntityMap = Map.tryFind (List.at 1 address) groupMap
                match optEntityMap with
                | None -> None
                | Some entityMap -> Map.tryFind (List.at 2 address) entityMap

        static member private entityAdder (address : Address) world (child : Entity) =
            let optGroupMap = Map.tryFind (List.at 0 address) world.Entities
            match optGroupMap with
            | None ->
                let entityMap = Map.singleton (List.at 2 address) child
                let groupMap = Map.singleton (List.at 1 address) entityMap
                { world with Entities = Map.add (List.at 0 address) groupMap world.Entities }
            | Some groupMap ->
                let optEntityMap = Map.tryFind (List.at 1 address) groupMap
                match optEntityMap with
                | None ->
                    let entityMap = Map.singleton (List.at 2 address) child
                    let groupMap = Map.add (List.at 1 address) entityMap groupMap
                    { world with Entities = Map.add (List.at 0 address) groupMap world.Entities }
                | Some entityMap ->
                    let entityMap = Map.add (List.at 2 address) child entityMap
                    let groupMap = Map.add (List.at 1 address) entityMap groupMap
                    { world with Entities = Map.add (List.at 0 address) groupMap world.Entities }

        static member private entityRemover (address : Address) world =
            let optGroupMap = Map.tryFind (List.at 0 address) world.Entities
            match optGroupMap with
            | None -> world
            | Some groupMap ->
                let optEntityMap = Map.tryFind (List.at 1 address) groupMap
                match optEntityMap with
                | None -> world
                | Some entityMap ->
                    let entityMap = Map.remove (List.at 2 address) entityMap
                    let groupMap = Map.add (List.at 1 address) entityMap groupMap
                    { world with Entities = Map.add (List.at 0 address) groupMap world.Entities }

        static member getEntity address world = Option.get <| World.optEntityFinder address world
        static member private setEntityWithoutEvent address entity world = World.entityAdder address world entity
        static member setEntity address entity world = 
                let oldEntity = Option.get <| World.optEntityFinder address world
                let world = World.entityAdder address world entity
                World.publish4 (ChangeEventName @ address) address (EntityChangeData { OldEntity = oldEntity }) world

        static member getOptEntity address world = World.optEntityFinder address world
        static member containsEntity address world = Option.isSome <| World.getOptEntity address world
        static member private setOptEntityWithoutEvent address optEntity world =
            match optEntity with 
            | None -> World.entityRemover address world
            | Some entity -> World.entityAdder address world entity

        static member withEntity fn address world = Sim.withSimulant World.getEntity World.setEntity fn address world
        static member withEntityAndWorld fn address world = Sim.withSimulantAndWorld World.getEntity World.setEntity fn address world
        static member tryWithEntity fn address world = Sim.tryWithSimulant World.getOptEntity World.setEntity fn address world
        static member tryWithEntityAndWorld fn address world = Sim.tryWithSimulantAndWorld World.getOptEntity World.setEntity fn address world
    
        static member getEntities address world =
            match address with
            | [screenStr; groupStr] ->
                match Map.tryFind screenStr world.Entities with
                | None -> Map.empty
                | Some groupMap ->
                    match Map.tryFind groupStr groupMap with
                    | None -> Map.empty
                    | Some entityMap -> entityMap
            | _ -> failwith <| "Invalid entity address '" + addrToStr address + "'."

        static member registerEntity address (entity : Entity) world =
            Entity.register address entity world

        static member unregisterEntity address world =
            let entity = World.getEntity address world
            Entity.unregister address entity world

        static member removeEntityImmediate (address : Address) world =
            let world = World.publish4 (RemovingEventName @ address) address NoData world
            let world = World.unregisterEntity address world
            World.setOptEntityWithoutEvent address None world

        static member removeEntity address world =
            let task =
                { ScheduledTime = world.TickTime
                  Operation = fun world -> if World.containsEntity address world then World.removeEntityImmediate address world else world }
            { world with Tasks = task :: world.Tasks }

        static member clearEntitiesImmediate (address : Address) world =
            let entities = World.getEntities address world
            Map.fold
                (fun world entityName _ -> World.removeEntityImmediate (address @ [entityName]) world)
                world
                entities

        static member clearEntities (address : Address) world =
            let entities = World.getEntities address world
            Map.fold
                (fun world entityName _ -> World.removeEntity (address @ [entityName]) world)
                world
                entities

        static member removeEntitiesImmediate (screenAddress : Address) entityNames world =
            List.fold
                (fun world entityName -> World.removeEntityImmediate (screenAddress @ [entityName]) world)
                world
                entityNames

        static member removeEntities (screenAddress : Address) entityNames world =
            List.fold
                (fun world entityName -> World.removeEntity (screenAddress @ [entityName]) world)
                world
                entityNames

        static member addEntity address entity world =
            let world =
                match World.getOptEntity address world with
                | None -> world
                | Some _ -> World.removeEntityImmediate address world
            let world = World.setEntityWithoutEvent address entity world
            let world = World.registerEntity address entity world
            World.publish4 (AddEventName @ address) address NoData world

        static member addEntities groupAddress entities world =
            List.fold
                (fun world (entity : Entity) -> World.addEntity (addrstr groupAddress entity.Name) entity world)
                world
                entities