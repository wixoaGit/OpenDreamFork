﻿using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows;
using System.Windows.Input;
using OpenDreamShared.Interface;
using OpenDreamClient.Renderer;
using OpenDreamClient.Dream;
using OpenDreamShared.Net.Packets;
using OpenDreamShared.Dream;

namespace OpenDreamClient.Interface.Elements {
    class ElementMap : Grid, IElement {
        public ElementDescriptor ElementDescriptor {
            get => _elementDescriptor;
            set {
                _elementDescriptor = (ElementDescriptorMap)value;
            }
        }

        private ElementDescriptorMap _elementDescriptor;
        private DreamRenderer _dreamRenderer;

        public ElementMap() {
            _dreamRenderer = new DreamRenderer();
            this.Children.Add(_dreamRenderer.OpenGLViewControl);
            
            this.Focusable = true;
            this.IsEnabled = true;
            this.UseLayoutRounding = true;
            this.Background = Brushes.Black;

            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
            this.MouseLeftButtonDown += OnLeftMouseDown;
        }

        public void UpdateVisuals() {
            
        }

        private (int X, int Y) ControlToScreenCoordinates(double x, double y) {
            return ((int)Math.Floor(x), (int)_dreamRenderer.OpenGLViewControl.Height - (int)Math.Floor(y));
        }

        private (int X, int Y) ScreenToWorldCoordinates(int x, int y) {
            int viewATOMX = (int)(x / 32);
            int viewATOMY = (int)(y / 32);

            return (_dreamRenderer.CameraX - 7 + viewATOMX, _dreamRenderer.CameraY - 7 + viewATOMY);
        }

        private void OnLoaded(object sender, RoutedEventArgs e) {
            _dreamRenderer.SetViewportSize(480, 480);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) {
            
        }

        private void OnLeftMouseDown(object sender, MouseEventArgs e) {
            Point mousePosition = e.GetPosition(_dreamRenderer.OpenGLViewControl);
            if (mousePosition.X < 0 || mousePosition.X > _dreamRenderer.OpenGLViewControl.Width ||
                mousePosition.Y < 0 || mousePosition.Y > _dreamRenderer.OpenGLViewControl.Height) return;
            (int X, int Y) screenCoordinates = ControlToScreenCoordinates(mousePosition.X, mousePosition.Y);

            ATOM clickedATOM = null;
            int iconX = 0, iconY = 0;
            ScreenLocation screenLocation = new ScreenLocation(screenCoordinates.X, screenCoordinates.Y, 32);

            foreach (ATOM screenObject in Program.OpenDream.ScreenObjects) {
                System.Drawing.Point atomScreenCoordinates = screenObject.ScreenLocation.GetScreenCoordinates(32);
                System.Drawing.Rectangle iconRect = new(atomScreenCoordinates, new System.Drawing.Size(32, 32));

                if (iconRect.Contains(new System.Drawing.Point(screenCoordinates.X, screenCoordinates.Y))) {
                    int screenObjectIconX = screenCoordinates.X - iconRect.X;
                    int screenObjectIconY = 32 - (screenCoordinates.Y - iconRect.Y);

                    if (screenObject.Icon.GetPixel(screenObjectIconX, screenObjectIconY).A != 0) {
                        clickedATOM = screenObject;
                        iconX = screenObjectIconX;
                        iconY = 32 - screenObjectIconY;
                    }
                }
            }

            if (clickedATOM == null) {
                (int X, int Y) worldCoordinates = ScreenToWorldCoordinates(screenCoordinates.X, screenCoordinates.Y);

                if (Program.OpenDream.Map.IsValidCoordinate(worldCoordinates.X, worldCoordinates.Y)) {
                    ATOM turf = Program.OpenDream.Map.Turfs[worldCoordinates.X, worldCoordinates.Y];

                    if (turf != null) {
                        iconX = (int)mousePosition.X % 32;
                        iconY = 32 - ((int)mousePosition.Y % 32);

                        foreach (ATOM atom in turf.Contents) {
                            bool isAbove = (clickedATOM == null || clickedATOM.Icon.Appearance.Layer <= atom.Icon.Appearance.Layer);

                            if (isAbove && atom.Icon.Appearance.Invisibility <= 0 && atom.Icon.GetPixel(iconX, 32 - iconY).A != 0) {
                                clickedATOM = atom;
                            }
                        }

                        if (clickedATOM == null) clickedATOM = turf;
                    }
                }
            }

            if (clickedATOM == null) return;
            e.Handled = true;
            this.Focus();

            PacketClickAtom pClickAtom = new PacketClickAtom(clickedATOM.ID, iconX, iconY, screenLocation);
            pClickAtom.ModifierShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            pClickAtom.ModifierCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            pClickAtom.ModifierAlt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            Program.OpenDream.Connection.SendPacket(pClickAtom);
        }
    }
}
