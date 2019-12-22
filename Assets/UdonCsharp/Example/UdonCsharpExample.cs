using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UdonCsharpExample : MonoBehaviour {
    [SerializeField, SerializePrivateVariables]
    public int Count, b = 0;
    Transform Child;

    // Start is called before the first frame update
    void Start() {
        Child = transform.Find("Child");
    }

    // Update is called once per frame
    void Update() {
        Count++;
        if (Count % 2 == 0) Child.Rotate(new Vector3(1, 0, 0), 10);
    }
}
