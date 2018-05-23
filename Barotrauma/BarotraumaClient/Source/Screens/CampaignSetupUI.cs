﻿using Microsoft.Xna.Framework;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    class CampaignSetupUI
    {
        private GUIComponent newGameContainer, loadGameContainer;

        private GUIListBox subList;
        private GUIListBox saveList;

        private GUITextBox saveNameBox, seedBox;

        private GUIButton loadGameButton;
        
        public Action<Submarine, string, string> StartNewGame;
        public Action<string> LoadGame;

        private bool isMultiplayer;

        public CampaignSetupUI(bool isMultiplayer, GUIComponent newGameContainer, GUIComponent loadGameContainer)
        {
            this.isMultiplayer = isMultiplayer;
            this.newGameContainer = newGameContainer;
            this.loadGameContainer = loadGameContainer;

            // New game left side
            new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.1f), newGameContainer.RectTransform, minSize: new Point(100, 30))
            {
                RelativeOffset = new Vector2(0.05f, 0.1f)
            }, TextManager.Get("SelectedSub") + ":");
            subList = new GUIListBox(new RectTransform(new Vector2(0.35f, 0.65f), newGameContainer.RectTransform)
            {
                RelativeOffset = new Vector2(0.05f, 0.25f)
            });

            UpdateSubList();

            // New game right side
            new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.1f), newGameContainer.RectTransform, Anchor.TopCenter, Pivot.TopLeft, minSize: new Point(100, 30))
            {
                AbsoluteOffset = new Point(40, 40)
            }, TextManager.Get("SaveName") + ":");

            saveNameBox = new GUITextBox(new RectTransform(new Vector2(0.4f, 0.1f), newGameContainer.RectTransform, Anchor.TopCenter, Pivot.TopLeft, minSize: new Point(100, 30))
            {
                AbsoluteOffset = new Point(40, 40),
                RelativeOffset = new Vector2(0, 0.1f)
            }, string.Empty);

            new GUITextBlock(new RectTransform(new Vector2(0.4f, 0.1f), newGameContainer.RectTransform, Anchor.TopCenter, Pivot.TopLeft, minSize: new Point(100, 30))
            {
                AbsoluteOffset = new Point(40, 40),
                RelativeOffset = new Vector2(0, 0.3f)
            }, TextManager.Get("MapSeed") + ":");

            seedBox = new GUITextBox(new RectTransform(new Vector2(0.4f, 0.1f), newGameContainer.RectTransform, Anchor.TopCenter, Pivot.TopLeft, minSize: new Point(100, 30))
            {
                AbsoluteOffset = new Point(40, 40),
                RelativeOffset = new Vector2(0, 0.4f)
            }, string.Empty);

            seedBox.Text = ToolBox.RandomSeed(8);

            var startButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.1f), newGameContainer.RectTransform, Anchor.BottomRight, minSize: new Point(80, 30))
            {
                AbsoluteOffset = new Point(40, 40)
            }, TextManager.Get("StartCampaignButton"));
            startButton.OnClicked = (GUIButton btn, object userData) =>
            {
                if (string.IsNullOrWhiteSpace(saveNameBox.Text))
                {
                    saveNameBox.Flash(Color.Red);
                    return false;
                }
                
                Submarine selectedSub = subList.SelectedData as Submarine;
                if (selectedSub == null) return false;

                string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer, saveNameBox.Text);
                if (selectedSub.HasTag(SubmarineTag.Shuttle) || !selectedSub.CompatibleContentPackages.Contains(GameMain.SelectedPackage.Name))
                {
                    if (!selectedSub.CompatibleContentPackages.Contains(GameMain.SelectedPackage.Name))
                    {
                        var msgBox = new GUIMessageBox(TextManager.Get("ContentPackageMismatch"),
                            TextManager.Get("ContentPackageMismatchWarning")
                                .Replace("[selectedcontentpackage]", GameMain.SelectedPackage.Name),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                        msgBox.Buttons[0].OnClicked = msgBox.Close;
                        msgBox.Buttons[0].OnClicked += (button, obj) => 
                        {
                            if (GUIMessageBox.MessageBoxes.Count == 0) StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                            return true;
                        };

                        msgBox.Buttons[1].OnClicked = msgBox.Close;
                    }

                    if (selectedSub.HasTag(SubmarineTag.Shuttle))
                    {
                        var msgBox = new GUIMessageBox(TextManager.Get("ShuttleSelected"),
                            TextManager.Get("ShuttleWarning"),
                            new string[] { TextManager.Get("Yes"), TextManager.Get("No") });

                        msgBox.Buttons[0].OnClicked = (button, obj) => { StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text); return true; };
                        msgBox.Buttons[0].OnClicked += msgBox.Close;

                        msgBox.Buttons[1].OnClicked = msgBox.Close;
                        return false;
                    }
                }
                else
                {
                    StartNewGame?.Invoke(selectedSub, savePath, seedBox.Text);
                }

                return true;
            };

            UpdateLoadMenu();
        }

        public void CreateDefaultSaveName()
        {
            string savePath = SaveUtil.CreateSavePath(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);
            saveNameBox.Text = Path.GetFileNameWithoutExtension(savePath);
        }

        public void UpdateSubList()
        {
            var subsToShow = Submarine.SavedSubmarines.Where(s => !s.HasTag(SubmarineTag.HideInMenus));

            subList.ClearChildren();

            foreach (Submarine sub in subsToShow)
            {
                var textBlock = new GUITextBlock(
                    new RectTransform(new Vector2(1, 0.1f), subList.RectTransform)
                    {
                        AbsoluteOffset = new Point(10, 0)
                    },
                    ToolBox.LimitString(sub.Name, GUI.Font, subList.Rect.Width - 65), style: "ListBoxElement")
                    {
                        ToolTip = sub.Description,
                        UserData = sub
                    };
                subList.AddChild(textBlock);

                if (sub.HasTag(SubmarineTag.Shuttle))
                {
                    textBlock.TextColor = textBlock.TextColor * 0.85f;

                    var shuttleText = new GUITextBlock(new RectTransform(new Point(100, textBlock.Rect.Height), textBlock.RectTransform, Anchor.CenterLeft)
                    {
                        IsFixedSize = false,
                        RelativeOffset = new Vector2(0.5f, 0)
                    },
                        TextManager.Get("Shuttle"), font: GUI.SmallFont)
                    {
                        TextColor = textBlock.TextColor * 0.8f,
                        ToolTip = textBlock.ToolTip
                    };
                }

                var infoButton = new GUIButton(new RectTransform(new Vector2(0.12f, 1), textBlock.RectTransform, Anchor.CenterRight), text: "?")
                {
                    UserData = sub
                };
                infoButton.OnClicked += (component, userdata) =>
                {
                    // TODO: use relative size
                    ((Submarine)userdata).CreatePreviewWindow(new GUIMessageBox("", "", 550, 400));
                    return true;
                };
            }
            if (Submarine.SavedSubmarines.Count > 0) subList.Select(Submarine.SavedSubmarines[0]);
        }

        public void UpdateLoadMenu()
        {
            loadGameContainer.ClearChildren();

            string[] saveFiles = SaveUtil.GetSaveFiles(isMultiplayer ? SaveUtil.SaveType.Multiplayer : SaveUtil.SaveType.Singleplayer);

            saveList = new GUIListBox(new RectTransform(new Vector2(0.4f, 0.8f), loadGameContainer.RectTransform, Anchor.CenterLeft)
            {
                RelativeOffset = new Vector2(0.075f, 0)
            })
            {
                OnSelected = SelectSaveFile
            };

            foreach (string saveFile in saveFiles)
            {
                saveList.AddChild(new GUITextBlock(new RectTransform(new Vector2(1, 0.1f), saveList.RectTransform),
                    text: Path.GetFileNameWithoutExtension(saveFile), style: "ListBoxElement")
                    { UserData = saveFile });     
            }

            loadGameButton = new GUIButton(new RectTransform(new Vector2(0.2f, 0.1f), loadGameContainer.RectTransform, Anchor.BottomRight)
            {
                RelativeOffset = new Vector2(0.075f, 0.1f)
            }, TextManager.Get("LoadButton"));
            loadGameButton.OnClicked = (btn, obj) => 
            {
                if (string.IsNullOrWhiteSpace(saveList.SelectedData as string)) return false;
                LoadGame?.Invoke(saveList.SelectedData as string);
                return true;
            };
            loadGameButton.Enabled = false;
        }

        private bool SelectSaveFile(GUIComponent component, object obj)
        {
            string fileName = (string)obj;

            XDocument doc = SaveUtil.LoadGameSessionDoc(fileName);

            if (doc == null)
            {
                DebugConsole.ThrowError("Error loading save file \"" + fileName + "\". The file may be corrupted.");
                return false;
            }

            loadGameButton.Enabled = true;

            RemoveSaveFrame();

            string subName = doc.Root.GetAttributeString("submarine", "");
            string saveTime = doc.Root.GetAttributeString("savetime", "unknown");
            string mapseed = doc.Root.GetAttributeString("mapseed", "unknown");

            var saveFileFrame = new GUIFrame(new RectTransform(new Vector2(0.4f, 0.6f), loadGameContainer.RectTransform, Anchor.TopRight)
            {
                RelativeOffset = new Vector2(0.075f, 0.1f)
            }, color: Color.Black)
            {
                UserData = "savefileframe"
            };

            new GUITextBlock(new RectTransform(new Vector2(1, 0.2f), saveFileFrame.RectTransform, Anchor.TopCenter)
            {
                RelativeOffset = new Vector2(0, 0.05f)
            }, 
            Path.GetFileNameWithoutExtension(fileName), font: GUI.LargeFont, textAlignment: Alignment.Center);

            var layoutGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.8f, 0.5f), saveFileFrame.RectTransform, Anchor.Center)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            });

            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("Submarine")} : {subName}", font: GUI.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("LastSaved")} : {saveTime}", font: GUI.SmallFont);
            new GUITextBlock(new RectTransform(new Vector2(1, 0), layoutGroup.RectTransform), $"{TextManager.Get("MapSeed")} : {mapseed}", font: GUI.SmallFont);

            new GUIButton(new RectTransform(new Vector2(0.2f, 0.1f), saveFileFrame.RectTransform, Anchor.BottomCenter)
            {
                RelativeOffset = new Vector2(0, 0.1f)
            }, TextManager.Get("Delete"))
            {
                UserData = fileName,
                OnClicked = DeleteSave
            };

            return true;
        }

        private bool DeleteSave(GUIButton button, object obj)
        {
            string saveFile = obj as string;

            if (obj == null) return false;

            SaveUtil.DeleteSave(saveFile);

            UpdateLoadMenu();

            return true;
        }

        private void RemoveSaveFrame()
        {
            GUIComponent prevFrame = null;
            foreach (GUIComponent child in loadGameContainer.Children)
            {
                if (child.UserData as string != "savefileframe") continue;

                prevFrame = child;
                break;
            }
            loadGameContainer.RemoveChild(prevFrame);
        }

    }
}
