namespace LibCommon
{
    public static class LayoutUtilities
    {
        public static void Uniform(
            int containerWidth, int containerHeight, 
            int contentWidth, int contentHeight, 
            out int contentX, out int contentY, 
            out int contentActualWidth, out int contentActualHeight)
        {
            contentActualWidth = contentWidth;
            contentActualHeight = contentHeight;

            if (contentActualWidth > containerWidth)
            {
                contentActualWidth = containerWidth;
                contentActualHeight = contentActualWidth * contentHeight / contentWidth;
            }

            if (contentActualHeight > containerHeight)
            {
                contentActualHeight = containerHeight;
                contentActualWidth = contentActualHeight * contentWidth / contentHeight;
            }

            contentX = (containerWidth - contentActualWidth) / 2;
            contentY = (containerHeight - contentActualHeight) / 2;
        }
    }
}
