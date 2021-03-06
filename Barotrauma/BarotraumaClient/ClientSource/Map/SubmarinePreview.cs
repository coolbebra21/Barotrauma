﻿using Barotrauma.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Barotrauma
{
    class SubmarinePreview : IDisposable
    {
        private SpriteRecorder spriteRecorder;
        private SubmarineInfo submarineInfo;
        private Camera camera;
        private Task loadTask;
        private volatile bool isDisposed;

        private GUIFrame previewFrame;

        private class HullCollection
        {
            public readonly List<Rectangle> Rects;
            public readonly string Name;

            public HullCollection(string identifier)
            {
                Rects = new List<Rectangle>();
                Name = TextManager.Get(identifier, returnNull: true) ?? identifier;
            }

            public void AddRect(XElement element)
            {
                Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
                rect.Y = -rect.Y;
                Rects.Add(rect);
            }
        }

        private struct Door
        {
            public readonly Rectangle Rect;

            public Door(Rectangle rect)
            {
                rect.Y = -rect.Y;
                Rect = rect;
            }
        }

        private Dictionary<string,HullCollection> hullCollections;
        private List<Door> doors;


        private static SubmarinePreview instance = null;

        public static void Create(SubmarineInfo submarineInfo)
        {
            instance?.Dispose();
            instance = new SubmarinePreview(submarineInfo);
        }

        private SubmarinePreview(SubmarineInfo subInfo)
        {
            camera = new Camera();
            submarineInfo = subInfo;
            spriteRecorder = new SpriteRecorder();
            isDisposed = false;
            loadTask = null;

            hullCollections = new Dictionary<string, HullCollection>();
            doors = new List<Door>();

            previewFrame = new GUIFrame(new RectTransform(Vector2.One, GUI.Canvas, Anchor.Center), style: null);
            new GUIFrame(new RectTransform(GUI.Canvas.RelativeSize, previewFrame.RectTransform, Anchor.Center), style: "GUIBackgroundBlocker");

            new GUIButton(new RectTransform(Vector2.One, previewFrame.RectTransform), "", style: null)
            {
                OnClicked = (btn, obj) => { Dispose(); return false; }
            };

            var innerFrame = new GUIFrame(new RectTransform(Vector2.One * 0.9f, previewFrame.RectTransform, Anchor.Center));
            var verticalLayout = new GUILayoutGroup(new RectTransform(Vector2.One * 0.95f, innerFrame.RectTransform, Anchor.Center), isHorizontal: false);
            var topLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.1f), verticalLayout.RectTransform, Anchor.Center), isHorizontal: true, childAnchor: Anchor.CenterLeft);

            new GUITextBlock(new RectTransform(new Vector2(0.95f, 1f), topLayout.RectTransform), subInfo.DisplayName, font: GUI.LargeFont);
            new GUIButton(new RectTransform(new Vector2(0.05f, 1f), topLayout.RectTransform), TextManager.Get("Close"))
            {
                OnClicked = (btn, obj) => { Dispose(); return false; }
            };

            new GUICustomComponent(new RectTransform(new Vector2(1f, 0.9f), verticalLayout.RectTransform, Anchor.Center),
                (spriteBatch, component) => { camera.UpdateTransform(true); RenderSubmarine(spriteBatch, component.Rect); },
                (deltaTime, component) => {
                    camera.MoveCamera(deltaTime, overrideMouseOn: component.Rect);
                    if (component.Rect.Contains(PlayerInput.MousePosition) &&
                        (PlayerInput.MidButtonHeld() || PlayerInput.LeftButtonHeld()))
                    {
                        Vector2 moveSpeed = PlayerInput.MouseSpeed * (float)deltaTime * 60.0f / camera.Zoom;
                        moveSpeed.X = -moveSpeed.X;
                        camera.Position += moveSpeed;
                    }
                });

            GeneratePreviewMeshes();
        }

        public static void AddToGUIUpdateList()
        {
            instance?.previewFrame?.AddToGUIUpdateList();
        }

        public Task GeneratePreviewMeshes()
        {
            if (loadTask != null) { throw new InvalidOperationException("Tried to start SubmarinePreview loadTask more than once!"); }
            loadTask = Task.Run(GeneratePreviewMeshesInternal);
            return loadTask;
        }

        private async Task GeneratePreviewMeshesInternal()
        {
            await Task.Yield();
            spriteRecorder.Begin(SpriteSortMode.BackToFront);

            HashSet<int> toIgnore = new HashSet<int>();

            foreach (var subElement in submarineInfo.SubmarineElement.Elements())
            {
                switch (subElement.Name.LocalName.ToLowerInvariant())
                {
                    case "item":
                        foreach (var component in subElement.Elements())
                        {
                            switch (component.Name.LocalName.ToLowerInvariant())
                            {
                                case "itemcontainer":
                                    ExtractItemContainerIds(component, toIgnore);
                                    break;
                                case "connectionpanel":
                                    ExtractConnectionPanelLinks(component, toIgnore);
                                    break;
                            }
                        }
                        break;
                }
                if (isDisposed) { return; }
                await Task.Yield();
            }

            foreach (var subElement in submarineInfo.SubmarineElement.Elements())
            {
                switch (subElement.Name.LocalName.ToLowerInvariant())
                {
                    case "item":
                        if (!toIgnore.Contains(subElement.GetAttributeInt("ID", 0)))
                        {
                            BakeMapEntity(subElement);
                        }
                        break;
                    case "structure":
                        BakeMapEntity(subElement);
                        break;
                    case "hull":
                        string identifier = subElement.GetAttributeString("roomname", "").ToLowerInvariant();
                        if (!string.IsNullOrEmpty(identifier))
                        {
                            HullCollection hullCollection = null;
                            if (!hullCollections.TryGetValue(identifier, out hullCollection))
                            {
                                hullCollection = new HullCollection(identifier);
                                hullCollections.Add(identifier, hullCollection);
                            }
                            hullCollection.AddRect(subElement);
                        }
                        break;
                }
                if (isDisposed) { return; }
                await Task.Yield();
            }
            spriteRecorder.End();

            camera.Position = (spriteRecorder.Min + spriteRecorder.Max) * 0.5f;
            float scaledSpan = (spriteRecorder.Max - spriteRecorder.Min).X / camera.Resolution.X;
            camera.Zoom = 0.8f / scaledSpan;
            camera.StopMovement();
        }

        private void ExtractItemContainerIds(XElement component, HashSet<int> ids)
        {
            string containedString = component.GetAttributeString("contained", "");
            string[] itemIdStrings = containedString.Split(',');
            for (int i = 0; i < itemIdStrings.Length; i++)
            {
                foreach (string idStr in itemIdStrings[i].Split(';'))
                {
                    if (!int.TryParse(idStr, out int id)) { continue; }
                    if (id != 0 && !ids.Contains(id)) { ids.Add(id); }
                }
            }
        }

        private void ExtractConnectionPanelLinks(XElement component, HashSet<int> ids)
        {
            var pins = component.Elements("input").Concat(component.Elements("output"));
            foreach (var pin in pins)
            {
                var links = pin.Elements("link");
                foreach (var link in links)
                {
                    int id = link.GetAttributeInt("w", 0);
                    if (id != 0 && !ids.Contains(id)) { ids.Add(id); }
                }
            }
        }

        private void BakeMapEntity(XElement element)
        {
            string identifier = element.GetAttributeString("identifier", "");
            if (string.IsNullOrEmpty(identifier)) { return; }
            Rectangle rect = element.GetAttributeRect("rect", Rectangle.Empty);
            if (rect.Equals(Rectangle.Empty)) { return; }

            float depth = element.GetAttributeFloat("spritedepth", 1f);
            bool flippedX = element.GetAttributeBool("flippedx", false);
            bool flippedY = element.GetAttributeBool("flippedy", false);

            float scale = element.GetAttributeFloat("scale", 1f);
            Color color = element.GetAttributeColor("spritecolor", Color.White);

            float rotation = element.GetAttributeFloat("rotation", 0f);

            MapEntityPrefab prefab = MapEntityPrefab.List.First(p => p.Identifier.Equals(identifier, StringComparison.OrdinalIgnoreCase));
            var texture = prefab.sprite.Texture;
            var srcRect = prefab.sprite.SourceRect;

            SpriteEffects spriteEffects = SpriteEffects.None;
            if (flippedX)
            {
                spriteEffects |= SpriteEffects.FlipHorizontally;
            }
            if (flippedY)
            {
                spriteEffects |= SpriteEffects.FlipVertically;
            }

            var prevEffects = prefab.sprite.effects;
            prefab.sprite.effects ^= spriteEffects;

            bool overrideSprite = false;
            ItemPrefab itemPrefab = prefab as ItemPrefab;
            StructurePrefab structurePrefab = prefab as StructurePrefab;
            if (itemPrefab != null)
            {
                BakeItemComponents(itemPrefab, rect, color, scale, rotation, depth, out overrideSprite);
            }

            if (!overrideSprite)
            {
                if (structurePrefab != null)
                {
                    ParseUpgrades(structurePrefab.ConfigElement, ref scale);

                    if (!prefab.ResizeVertical)
                    {
                        rect.Height = (int)(rect.Height * scale / prefab.Scale);
                    }
                    if (!prefab.ResizeHorizontal)
                    {
                        rect.Width = (int)(rect.Width * scale / prefab.Scale);
                    }
                    var textureScale = element.GetAttributeVector2("texturescale", Vector2.One);

                    Vector2 backGroundOffset = Vector2.Zero;

                    Vector2 textureOffset = element.GetAttributeVector2("textureoffset", Vector2.Zero);
                    if (flippedX) { textureOffset.X = -textureOffset.X; }
                    if (flippedY) { textureOffset.Y = -textureOffset.Y; }

                    backGroundOffset = new Vector2(
                                MathUtils.PositiveModulo((int)-textureOffset.X, prefab.sprite.SourceRect.Width),
                                MathUtils.PositiveModulo((int)-textureOffset.Y, prefab.sprite.SourceRect.Height));

                    prefab.sprite.DrawTiled(
                        spriteRecorder,
                        rect.Location.ToVector2() * new Vector2(1f, -1f),
                        rect.Size.ToVector2(),
                        color: color,
                        startOffset: backGroundOffset,
                        textureScale: textureScale * scale,
                        depth: depth);
                }
                else if (itemPrefab != null)
                {
                    ParseUpgrades(itemPrefab.ConfigElement, ref scale);

                    if (prefab.ResizeVertical || prefab.ResizeHorizontal)
                    {
                        if (!prefab.ResizeHorizontal)
                        {
                            rect.Width = (int)(prefab.sprite.size.X * scale);
                        }
                        if (!prefab.ResizeVertical)
                        {
                            rect.Height = (int)(prefab.sprite.size.Y * scale);
                        }

                        var spritePos = rect.Center.ToVector2();
                        //spritePos.Y = rect.Height - spritePos.Y;

                        prefab.sprite.DrawTiled(
                            spriteRecorder,
                            rect.Location.ToVector2() * new Vector2(1f, -1f),
                            rect.Size.ToVector2(),
                            color: color,
                            textureScale: Vector2.One * scale,
                            depth: depth);

                        foreach (var decorativeSprite in itemPrefab.DecorativeSprites)
                        {
                            float offsetState = 0f;
                            Vector2 offset = decorativeSprite.GetOffset(ref offsetState, Vector2.Zero) * scale;
                            if (flippedX && itemPrefab.CanSpriteFlipX) { offset.X = -offset.X; }
                            if (flippedY && itemPrefab.CanSpriteFlipY) { offset.Y = -offset.Y; }
                            decorativeSprite.Sprite.DrawTiled(spriteRecorder,
                                new Vector2(spritePos.X + offset.X - rect.Width / 2, -(spritePos.Y + offset.Y + rect.Height / 2)),
                                rect.Size.ToVector2(), color: color,
                                textureScale: Vector2.One * scale,
                                depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - prefab.sprite.Depth), 0.999f));
                        }
                    }
                    else
                    {
                        rect.Width = (int)(rect.Width * scale / prefab.Scale);
                        rect.Height = (int)(rect.Height * scale / prefab.Scale);

                        var spritePos = rect.Center.ToVector2();
                        spritePos.Y -= rect.Height;
                        //spritePos.Y = rect.Height - spritePos.Y;

                        prefab.sprite.Draw(
                            spriteRecorder,
                            spritePos * new Vector2(1f, -1f),
                            color,
                            prefab.sprite.Origin,
                            rotation,
                            scale,
                            prefab.sprite.effects, depth);

                        foreach (var decorativeSprite in itemPrefab.DecorativeSprites)
                        {
                            float rotationState = 0f; float offsetState = 0f;
                            float rot = decorativeSprite.GetRotation(ref rotationState, 0f);
                            Vector2 offset = decorativeSprite.GetOffset(ref offsetState, Vector2.Zero) * scale;
                            if (flippedX && itemPrefab.CanSpriteFlipX) { offset.X = -offset.X; }
                            if (flippedY && itemPrefab.CanSpriteFlipY) { offset.Y = -offset.Y; }
                            decorativeSprite.Sprite.Draw(spriteRecorder, new Vector2(spritePos.X + offset.X, -(spritePos.Y + offset.Y)), color,
                                MathHelper.ToRadians(rotation) + rot, decorativeSprite.GetScale(0f) * scale, prefab.sprite.effects,
                                depth: Math.Min(depth + (decorativeSprite.Sprite.Depth - prefab.sprite.Depth), 0.999f));
                        }
                    }
                }
            }

            prefab.sprite.effects = prevEffects;
        }

        private void BakeItemComponents(
            ItemPrefab prefab,
            Rectangle rect, Color color,
            float scale, float rotation, float depth,
            out bool overrideSprite)
        {
            overrideSprite = false;

            foreach (var subElement in prefab.ConfigElement.Elements())
            {
                switch (subElement.Name.LocalName.ToLowerInvariant())
                {
                    case "turret":
                        Sprite barrelSprite = null;
                        Sprite railSprite = null;
                        foreach (XElement turretSubElem in subElement.Elements())
                        {
                            switch (turretSubElem.Name.ToString().ToLowerInvariant())
                            {
                                case "barrelsprite":
                                    barrelSprite = new Sprite(turretSubElem);
                                    break;
                                case "railsprite":
                                    railSprite = new Sprite(turretSubElem);
                                    break;
                            }
                        }

                        var transformedBarrelPos = MathUtils.RotatePointAroundTarget(
                            subElement.GetAttributeVector2("barrelpos", Vector2.Zero) * scale,
                            new Vector2(rect.Width / 2, rect.Height / 2),
                            MathHelper.ToRadians(rotation));

                        Vector2 drawPos = new Vector2(rect.X + transformedBarrelPos.X, rect.Y - transformedBarrelPos.Y);
                        drawPos.Y = -drawPos.Y;

                        railSprite?.Draw(spriteRecorder,
                            drawPos,
                            color,
                            rotation + MathHelper.PiOver2, scale,
                            SpriteEffects.None, depth + (railSprite.Depth - prefab.sprite.Depth));

                        barrelSprite?.Draw(spriteRecorder,
                            drawPos - new Vector2((float)Math.Cos(MathHelper.ToRadians(rotation)), (float)Math.Sin(MathHelper.ToRadians(rotation))) * scale,
                            color,
                            rotation + MathHelper.PiOver2, scale,
                            SpriteEffects.None, depth + (barrelSprite.Depth - prefab.sprite.Depth));

                        break;
                    case "door":
                        doors.Add(new Door(rect));

                        var doorSpriteElem = subElement.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("sprite", StringComparison.OrdinalIgnoreCase));
                        if (doorSpriteElem != null)
                        {
                            string texturePath = subElement.GetAttributeString("texture", "");
                            Vector2 pos = rect.Location.ToVector2() * new Vector2(1f, -1f);
                            if (subElement.GetAttributeBool("horizontal", false))
                            {
                                pos.Y += (float)rect.Height * 0.5f;
                            }
                            else
                            {
                                pos.X += (float)rect.Width * 0.5f;
                            }
                            Sprite doorSprite = new Sprite(doorSpriteElem, texturePath.Contains("/") ? "" : Path.GetDirectoryName(prefab.FilePath));
                            spriteRecorder.Draw(doorSprite.Texture, pos,
                                new Rectangle((int)doorSprite.SourceRect.X,
                                    (int)doorSprite.SourceRect.Y,
                                    (int)doorSprite.size.X, (int)doorSprite.size.Y),
                                color, 0.0f, doorSprite.Origin, new Vector2(scale), SpriteEffects.None, doorSprite.Depth);
                        }
                        break;
                    case "ladder":
                        var backgroundSprElem = subElement.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("backgroundsprite", StringComparison.OrdinalIgnoreCase));
                        if (backgroundSprElem != null)
                        {
                            Sprite backgroundSprite = new Sprite(backgroundSprElem);
                            backgroundSprite.DrawTiled(spriteRecorder,
                                new Vector2(rect.Left, -rect.Top) - backgroundSprite.Origin * scale,
                                new Vector2(backgroundSprite.size.X * scale, rect.Height), color: color,
                                textureScale: Vector2.One * scale,
                                depth: depth + 0.1f);
                        }
                        break;
                }
            }
        }

        public void ParseUpgrades(XElement prefabConfigElement, ref float scale)
        {
            foreach (var upgrade in prefabConfigElement.Elements("Upgrade"))
            {
                var upgradeVersion = new Version(upgrade.GetAttributeString("gameversion", "0.0.0.0"));
                if (upgradeVersion >= submarineInfo.GameVersion)
                {
                    string scaleModifier = upgrade.GetAttributeString("scale", "*1");

                    if (scaleModifier.StartsWith("*"))
                    {
                        scale *= float.Parse(scaleModifier.Substring(1));
                    }
                    else
                    {
                        scale = float.Parse(scaleModifier);
                    }
                }
            }
        }

        private void RenderSubmarine(SpriteBatch spriteBatch, Rectangle scissorRectangle)
        {
            if (spriteRecorder == null) { return; }

            GUI.DrawRectangle(spriteBatch, scissorRectangle, new Color(0.051f, 0.149f, 0.271f, 1.0f), isFilled: true);
            
            if (!spriteRecorder.ReadyToRender)
            {
                string waitText = "Generating preview...";
                GUI.Font.DrawString(
                    spriteBatch,
                    waitText,
                    scissorRectangle.Center.ToVector2(),
                    Color.White,
                    0f,
                    GUI.Font.MeasureString(waitText) * 0.5f,
                    1f,
                    SpriteEffects.None,
                    0f);
                return;
            }
            spriteBatch.End();

            var prevScissorRect = GameMain.Instance.GraphicsDevice.ScissorRectangle;
            GameMain.Instance.GraphicsDevice.ScissorRectangle = scissorRectangle;

            spriteRecorder.Render(camera);

            var mousePos = camera.ScreenToWorld(PlayerInput.MousePosition);
            mousePos.Y = -mousePos.Y;

            spriteBatch.Begin(SpriteSortMode.BackToFront, rasterizerState: GameMain.ScissorTestEnable, transformMatrix: camera.Transform);
            GameMain.Instance.GraphicsDevice.ScissorRectangle = scissorRectangle;
            foreach (var hullCollection in hullCollections.Values)
            {
                bool mouseOver = false;

                foreach (var rect in hullCollection.Rects)
                {
                    mouseOver = rect.Contains(mousePos);
                    if (mouseOver) { break; }
                }

                foreach (var rect in hullCollection.Rects)
                {
                    GUI.DrawRectangle(spriteBatch, rect, mouseOver ? Color.Red : Color.Blue, depth: mouseOver ? 0.45f : 0.5f, thickness: (mouseOver ? 4f : 2f) / camera.Zoom);
                }

                if (mouseOver)
                {
                    string str = hullCollection.Name;
                    Vector2 strSize = GUI.Font.MeasureString(str) / camera.Zoom;
                    Vector2 padding = new Vector2(30, 30) / camera.Zoom;
                    Vector2 shift = new Vector2(10, 0) / camera.Zoom;

                    GUI.DrawRectangle(spriteBatch, mousePos + shift, strSize + padding, Color.Black, isFilled: true, depth: 0.25f);
                    GUI.Font.DrawString(spriteBatch, str, mousePos + shift + (strSize + padding) * 0.5f, Color.White, 0f, strSize * camera.Zoom * 0.5f, 1f / camera.Zoom, SpriteEffects.None, 0f);
                }
            }
            foreach (var door in doors)
            {
                GUI.DrawRectangle(spriteBatch, door.Rect, GUI.Style.Green * 0.5f, isFilled: true, depth: 0.4f);
            }
            spriteBatch.End();

            GameMain.Instance.GraphicsDevice.ScissorRectangle = prevScissorRect;
            spriteBatch.Begin(SpriteSortMode.Deferred);
        }

        public void Dispose()
        {
            previewFrame = null;
            spriteRecorder?.Dispose();
            isDisposed = true;
        }
    }
}
