// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;

namespace UnityEditor.Experimental.GraphView
{
    public class FreehandSelector : MouseManipulator
    {
        private readonly FreehandElement m_FreehandElement;
        private bool m_Active;
        private GraphView m_GraphView;

        public FreehandSelector()
        {
            activators.Add(new ManipulatorActivationFilter {button = MouseButton.LeftMouse, modifiers = EventModifiers.Shift});
            activators.Add(new ManipulatorActivationFilter {button = MouseButton.LeftMouse, modifiers = EventModifiers.Shift | EventModifiers.Alt});
            m_FreehandElement = new FreehandElement();
            m_FreehandElement.StretchToParentSize();
        }

        protected override void RegisterCallbacksOnTarget()
        {
            m_GraphView = target as GraphView;
            if (m_GraphView == null)
                throw new InvalidOperationException("Manipulator can only be added to a GraphView");

            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<KeyDownEvent>(OnKeyDown);
            target.RegisterCallback<KeyUpEvent>(OnKeyUp);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<KeyDownEvent>(OnKeyDown);
            target.UnregisterCallback<KeyUpEvent>(OnKeyUp);

            m_GraphView = null;
        }

        private void OnMouseDown(MouseDownEvent e)
        {
            if (m_Active)
            {
                e.StopImmediatePropagation();
                return;
            }

            if (CanStartManipulation(e))
            {
                m_GraphView.ClearSelection();

                m_GraphView.Add(m_FreehandElement);

                m_FreehandElement.points.Clear();
                m_FreehandElement.points.Add(e.localMousePosition);
                m_FreehandElement.deleteModifier = e.altKey;

                m_Active = true;
                target.CaptureMouse();
                e.StopImmediatePropagation();
            }
        }

        private void OnMouseUp(MouseUpEvent e)
        {
            if (!m_Active || !CanStopManipulation(e))
                return;

            m_GraphView.Remove(m_FreehandElement);

            m_FreehandElement.points.Add(e.localMousePosition);

            List<ISelectable> selection = m_GraphView.selection;

            // a copy is necessary because Add To selection might cause a SendElementToFront which will change the order.
            List<ISelectable> newSelection = new List<ISelectable>();
            m_GraphView.graphElements.ForEach(element =>
            {
                if (element.IsSelectable())
                {
                    for (int i = 1; i < m_FreehandElement.points.Count; i++)
                    {
                        // Apply offset
                        Vector2 start = m_GraphView.ChangeCoordinatesTo(element, m_FreehandElement.points[i - 1]);
                        Vector2 end = m_GraphView.ChangeCoordinatesTo(element, m_FreehandElement.points[i]);
                        float minx = Mathf.Min(start.x, end.x);
                        float maxx = Mathf.Max(start.x, end.x);
                        float miny = Mathf.Min(start.y, end.y);
                        float maxy = Mathf.Max(start.y, end.y);

                        var rect = new Rect(minx, miny, maxx - minx + 1, maxy - miny + 1);
                        if (element.Overlaps(rect))
                        {
                            newSelection.Add(element);
                            break;
                        }
                    }
                }
            });

            foreach (ISelectable selectable in newSelection)
            {
                if (!selection.Contains(selectable))
                    m_GraphView.AddToSelection(selectable);
            }

            if (e.altKey)
            {
                // Delete instead
                m_GraphView.DeleteSelection();
            }

            m_Active = false;
            target.ReleaseMouse();
            e.StopPropagation();
        }

        private void OnMouseMove(MouseMoveEvent e)
        {
            if (!m_Active)
                return;

            m_FreehandElement.points.Add(e.localMousePosition);
            m_FreehandElement.deleteModifier = e.altKey;

            e.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent e)
        {
            if (m_Active)
                m_FreehandElement.deleteModifier = e.altKey;
        }

        private void OnKeyUp(KeyUpEvent e)
        {
            if (m_Active)
                m_FreehandElement.deleteModifier = e.altKey;
        }

        private class FreehandElement : ImmediateModeElement
        {
            private List<Vector2> m_Points = new List<Vector2>();
            public List<Vector2> points { get { return m_Points; } }

            public FreehandElement()
            {
                RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            }

            private bool m_DeleteModifier;
            public bool deleteModifier
            {
                private get { return m_DeleteModifier; }
                set
                {
                    if (m_DeleteModifier == value)
                        return;
                    m_DeleteModifier = value;
                    MarkDirtyRepaint();
                }
            }

            private static CustomStyleProperty<float> s_SegmentSizeProperty = new CustomStyleProperty<float>("--segment-size");
            private static CustomStyleProperty<Color> s_SegmentColorProperty = new CustomStyleProperty<Color>("--segment-color");
            private static CustomStyleProperty<Color> s_DeleteSegmentColorProperty = new CustomStyleProperty<Color>("--delete-segment-color");

            float m_SegmentSize = 5f;
            public float segmentSize { get { return m_SegmentSize; } }

            Color m_SegmentColor = new Color(1f, 0.6f, 0f);
            public Color segmentColor { get { return m_SegmentColor; } }

            Color m_DeleteSegmentColor = new Color(1f, 0f, 0f);
            public Color deleteSegmentColor { get { return m_DeleteSegmentColor; } }

            private void OnCustomStyleResolved(CustomStyleResolvedEvent e)
            {
                ICustomStyle styles = e.customStyle;
                float segmentSizeValue = 0f;
                Color segmentColorValue = Color.clear;
                Color deleteColorValue = Color.clear;

                if (styles.TryGetValue(s_SegmentSizeProperty, out segmentSizeValue))
                    m_SegmentSize = segmentSizeValue;

                if (styles.TryGetValue(s_SegmentColorProperty, out segmentColorValue))
                    m_SegmentColor = segmentColorValue;

                if (styles.TryGetValue(s_DeleteSegmentColorProperty, out deleteColorValue))
                    m_DeleteSegmentColor = deleteColorValue;
            }

            protected override void ImmediateRepaint()
            {
                var pointCount = points.Count;
                if (pointCount < 1)
                    return;

                var lineColor = (deleteModifier) ? deleteSegmentColor : segmentColor;

                HandleUtility.ApplyWireMaterial();

                GL.Begin(GL.LINES);
                GL.Color(lineColor);

                for (int i = 1; i < pointCount; i++)
                {
                    // Apply offset
                    Vector2 start = points[i - 1] + parent.layout.position;
                    Vector2 end = points[i] + parent.layout.position;

                    DrawDottedLine(start, end, segmentSize);
                }

                GL.End();
            }

            private void DrawDottedLine(Vector3 p1, Vector3 p2, float segmentsLength)
            {
                float length = Vector3.Distance(p1, p2); // ignore z component
                int count = Mathf.CeilToInt(length / segmentsLength);
                for (int i = 0; i < count; i += 2)
                {
                    GL.Vertex((Vector3.Lerp(p1, p2, i * segmentsLength / length)));
                    GL.Vertex((Vector3.Lerp(p1, p2, (i + 1) * segmentsLength / length)));
                }
            }
        }
    }
}
