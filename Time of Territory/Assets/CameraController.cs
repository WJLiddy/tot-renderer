using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{
    readonly float SCROLL_AREA = 0.02f;
    readonly float SCROLL_SPEED = 35f;
    readonly float CAM_MAX = 240f;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {

        if (Input.mousePosition.y > Screen.height * (1f - SCROLL_AREA))
        {
            this.transform.position += Vector3.back * (SCROLL_SPEED * Time.unscaledDeltaTime);
        }
        if (Input.mousePosition.y < Screen.height * (SCROLL_AREA))
        {
            this.transform.position += Vector3.forward * (SCROLL_SPEED * Time.unscaledDeltaTime);
        }

        if (Input.mousePosition.x > Screen.width * (1f - SCROLL_AREA))
        {
            this.transform.position += Vector3.left * (SCROLL_SPEED * Time.unscaledDeltaTime);
        }
        if (Input.mousePosition.x < Screen.width * (SCROLL_AREA))
        {
            this.transform.position += Vector3.right * (SCROLL_SPEED * Time.unscaledDeltaTime);
        }

        this.transform.position = new Vector3(Mathf.Max(-CAM_MAX, Mathf.Min(0, transform.position.x)), this.transform.position.y, Mathf.Min(CAM_MAX, Mathf.Max(0, transform.position.z)));

    }
}
