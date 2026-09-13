using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform Target;
    public float     SmoothTime = 0.12f;
    public Vector2   BoundsMin  = new Vector2(0f, 0f);
    public Vector2   BoundsMax  = new Vector2(80f, 60f);

    Vector3 _currentVel;
    Camera  _cam;

    void Awake() 
    { 
        _cam = GetComponent<Camera>(); 
    }

    void LateUpdate()
    {
        if (Target == null) return;
        Vector3 des = new Vector3(Target.position.x, Target.position.y, transform.position.z);
        float hH = _cam.orthographicSize;
        float hW = hH * _cam.aspect;

        //set boundary movement of camera
        des.x = Mathf.Clamp(des.x, BoundsMin.x + hW, BoundsMax.x - hW);
        des.y = Mathf.Clamp(des.y, BoundsMin.y + hH, BoundsMax.y - hH);
        

        transform.position = Vector3.SmoothDamp(transform.position, des, ref _currentVel, SmoothTime);
    }

    public void SetTarget(Transform t) 
    { 
        Target = t; 
    }
    public void SetBounds(Vector2 min, Vector2 max) 
    { 
        BoundsMin = min; BoundsMax = max; 
    }
}
