namespace Gpm.Ui
{
    using System;
    using UnityEngine;
    using UnityEngine.UI;
    using UnityEngine.Events;

    public partial class InfiniteScroll : IMoveScroll
    {
        public enum MoveToType
        {
            MOVE_TO_TOP = 0,
            MOVE_TO_CENTER,
            MOVE_TO_BOTTOM
        }

        public static class Control
        {
            public static void MoveTo(InfiniteScroll scroll, int itemIndex, MoveToType moveToType, float time = 0)
            {
                MoveTo(scroll, itemIndex, moveToType, new CurveFromType(CurveType.EASE_IN_OUT, time));
            }

            public static void MoveTo(InfiniteScroll scroll, float scrollRate, float time = 0)
            {
                MoveTo(scroll, scrollRate, new CurveFromType(CurveType.EASE_IN_OUT, time));
            }

            public static void MoveTo(InfiniteScroll scroll, int itemIndex, MoveToType moveToType, Curve curve)
            {
                ScrollMoveTo moveto = scroll.gameObject.GetComponent<ScrollMoveTo>();
                if (moveto == null)
                {
                    moveto = scroll.gameObject.AddComponent<ScrollMoveTo>();
                }

                moveto.Set(itemIndex, moveToType, curve.time);
                moveto.curve = curve.curve;

                moveto.autoDestory = true;

                moveto.Play();
            }
            
            public static void MoveTo(InfiniteScroll scroll, float scrollRate, Curve curve)
            {
                ScrollMoveTo moveto = scroll.gameObject.GetComponent<ScrollMoveTo>();
                if (moveto == null)
                {
                    moveto = scroll.gameObject.AddComponent<ScrollMoveTo>();
                }

                moveto.Set(scrollRate, curve.time);
                moveto.curve = curve.curve;

                moveto.autoDestory = true;

                moveto.Play();
            }
        }

        private const float UPDOWN_MULTIPLY = 2.0f;
        private const int NEEDITEM_MORE_LINE = 1;
        private const int NEEDITEM_EXTRA_ADD = 2;

        protected ScrollRect scrollRect = null;
        protected RectTransform viewport = null;

        protected float sizeInterpolationValue = 0.0001f; // 0.01%
        
        public void MoveTo(InfiniteScrollData data, MoveToType moveToType, float time = 0)
        {
            MoveTo(GetItemIndex(data), moveToType, new CurveFromType(CurveType.EASE_IN_OUT, time));
        }
        
        public void MoveTo(InfiniteScrollData data, MoveToType moveToType, Curve curve)
        {
            MoveTo(GetItemIndex(data), moveToType, curve);
        }

        public void MoveTo(int itemIndex, MoveToType moveToType, float time = 0)
        {
            MoveTo(itemIndex, moveToType, new CurveFromType(CurveType.EASE_IN_OUT, time));
        }
        
        public void MoveTo(int itemIndex, MoveToType moveToType, Curve curve)
        {
            if (isInitialize == false)
            {
                Initialize();
            }

            if (IsValidItemIndex(itemIndex) == true)
            {
                if (curve.time > 0)
                {
                    Control.MoveTo(this, itemIndex, moveToType, curve);
                }
                else
                {
                    SetScrollPosition(GetMovePosition(itemIndex, moveToType));
                    Control.MoveTo(this, itemIndex, moveToType, curve);
                }
            }
        }
        
        public void MoveTo(float scrollRate, float time = 0)
        {
            MoveTo(scrollRate, new CurveFromType(CurveType.EASE_IN_OUT, time));
        }
        
        public void MoveTo(float scrollRate, Curve curve)
        {
            if (isInitialize == false)
            {
                Initialize();
            }

            if (curve.time > 0)
            {
                Control.MoveTo(this, scrollRate, curve);
            }
            else
            {
                SetScrollPosition(GetMovePosition(scrollRate));
                Control.MoveTo(this, scrollRate, curve);
            }
        }

        public void MoveToFromDataIndex(int dataIndex, MoveToType moveToType, float time = 0)
        {
            MoveToFromDataIndex(dataIndex, moveToType, new CurveFromType(CurveType.EASE_IN_OUT, time));
        }
        
        public void MoveToFromDataIndex(int dataIndex, MoveToType moveToType, Curve curve)
        {
            if (isInitialize == false)
            {
                Initialize();
            }

            if (IsValidDataIndex(dataIndex) == true)
            {
                MoveTo(dataList[dataIndex].itemIndex, moveToType, curve);
            }
        }

        public void MoveToFirstData()
        {
            if (isInitialize == false)
            {
                Initialize();
            }

            UpdateContentSize();

            Vector2 normalizedPosition;
            if (layout.IsVertical() == true)
            {
                normalizedPosition = Vector2.one;
            }
            else
            {
                normalizedPosition = Vector2.zero;
            }

            if (scrollRect.normalizedPosition != normalizedPosition)
            {
                scrollRect.normalizedPosition = normalizedPosition;
                isUpdateArea = true;
            }

            MoveTo(0);
        }

        public void MoveToLastData()
        {
            if (isInitialize == false)
            {
                Initialize();
            }

            UpdateContentSize();

            Vector2 normalizedPosition;
            if (layout.IsVertical() == true)
            {
                normalizedPosition = Vector2.zero;
            }
            else
            {
                normalizedPosition = Vector2.one;
            }
            if (scrollRect.normalizedPosition != normalizedPosition)
            {
                scrollRect.normalizedPosition = normalizedPosition;
                isUpdateArea = true;
            }

            MoveTo(1);
        }

        public bool IsMoveToFirstData()
        {
            if (isInitialize == false)
            {
                Initialize();
            }

            if (NeedUpdateItem() == true)
            {
                UpdateShowItem(false);
            }

            float contentPosition = GetContentPosition();
            float viewportSize = GetViewportSize();
            float contentSize = GetContentSize();

            return IsMoveToFirstData(contentPosition, viewportSize, contentSize);
        }

        public bool IsMoveToLastData()
        {
            if (isInitialize == false)
            {
                Initialize();
            }

            float contentPosition = GetContentPosition();
            float viewportSize = GetViewportSize();
            float contentSize = GetContentSize();

            return IsMoveToLastData(contentPosition, viewportSize, contentSize);
        }

        public Vector2 GetMovePosition(float scrollRate)
        {
            float viewportSize = GetViewportSize();
            float contentSize = GetContentSize();

            float move = (contentSize - viewportSize) * Mathf.Clamp01(scrollRate);
            move = Math.Max(0.0f, move);

            move = ItemPostionFromOffset(move);

            return layout.GetAxisVector(content.anchoredPosition, move);
        }

        public Vector2 GetMovePosition(int itemIndex, MoveToType moveToType)
        {
            float move = GetMoveOffset(itemIndex, moveToType);

            return layout.GetAxisVector(content.anchoredPosition, move);
        }

        public Vector2 GetScrollPosition()
        {
            return content.anchoredPosition;
        }

        public void SetScrollPosition(Vector2 position)
        {
            content.anchoredPosition = position;

            float contentPosition = GetContentPosition();
            if (cachedData.contentPosition != contentPosition)
            {
                cachedData.contentPosition = contentPosition;
                isUpdateArea = true;
            }
        }

        public void SetScrollPosition(float movePosition)
        {
            Vector2 prevPosition = GetScrollPosition();

            SetScrollPosition(layout.GetAxisVector(prevPosition, movePosition));
        }

        public void ClearScrollContent()
        {
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = Vector2.zero;

            cachedData.Clear();

            CheckScrollData();
        }

        public void AddScrollValueChangedLisnter(UnityAction<Vector2> listener)
        {
            if (isInitialize == false)
            {
                Initialize();
            }

            scrollRect.onValueChanged.AddListener(listener);
        }

        private bool IsMoveToFirstData(float position, float viewportSize, float contentSize)
        {
            bool isShow = false;

            if (viewportSize > contentSize)
            {
                isShow = true;
            }
            else
            {
                float interpolation = contentSize * sizeInterpolationValue;
                if (-position > -interpolation)
                {
                    isShow = true;
                }
            }

            return isShow;
        }

        private bool IsMoveToLastData(float position, float viewportSize, float contentSize)
        {
            bool isShow = false;

            if (viewportSize > contentSize)
            {
                isShow = true;
            }
            else
            {
                float interpolation = contentSize * sizeInterpolationValue;

                if (-(viewportSize + position - contentSize) <= interpolation)
                {
                    isShow = true;
                }
            }

            return isShow;
        }

        protected float GetMoveOffset(int itemIndex, MoveToType moveToType)
        {
            float viewportSize = GetViewportSize();
            float contentSize = GetContentSize();

            float move = 0.0f;
            float itemSize = GetItemSize(itemIndex);
            float distance = GetItemDistance(itemIndex);

            move = distance;

            switch (moveToType)
            {
                case MoveToType.MOVE_TO_CENTER:
                    {
                        move -= viewportSize * 0.5f - itemSize * 0.5f;
                        break;
                    }
                case MoveToType.MOVE_TO_BOTTOM:
                    {
                        move -= viewportSize - itemSize;
                        break;
                    }
            }

            move = Mathf.Clamp(move, 0.0f, contentSize - viewportSize);
            move = Math.Max(0.0f, move);

            return ItemPostionFromOffset(move);
        }


        private void OnValueChanged(Vector2 value)
        {
            bool prevIsStartLine = isStartLine;
            isStartLine = IsMoveToFirstData();
            if (prevIsStartLine != isStartLine)
            {
                onStartLine.Invoke(isStartLine);

                changeValue = true;
            }

            bool prevIsEndLine = isEndLine;
            isEndLine = IsMoveToLastData();
            if (prevIsEndLine != isEndLine)
            {
                onEndLine.Invoke(isEndLine);

                changeValue = true;
            }

            UpdateShowItem();
        }
    }
}
