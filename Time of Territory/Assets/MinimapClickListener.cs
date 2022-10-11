using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class MinimapClickListener : MonoBehaviour, IPointerClickHandler
{
    //Detect if a click occurs - this is a callback, you do NOT have to call this
    public void OnPointerClick(PointerEventData eventData)
    {
        Vector2 clickPosition;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(GetComponent<RectTransform>(), eventData.position, eventData.pressEventCamera, out clickPosition))
            return;

        // add 64 to center it, then divide by 128 (width), then mult by 96 (unit)
        Vector2 gridPosition = new Vector2(96*((64f + clickPosition.x) / 128f), 96*((64f + clickPosition.y) / 128f));


        //offset ~5
        //so it's centered up
        Camera.main.transform.position = new Vector3(gridPosition.x * -2.5f, Camera.main.transform.position.y, (5+gridPosition.y) * 2.5f);
    }
}
