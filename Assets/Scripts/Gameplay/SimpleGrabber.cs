// using UnityEngine;

// public class SimpleGrabber : MonoBehaviour
// {
//     public Api api;
//     public float pickRange = 5f;
//     public LayerMask pickMask = ~0;

//     void Update()
//     {
//         // Debug raycasts
//         Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
//         Debug.DrawRay(ray.origin, ray.direction * pickRange, Color.red);

//         if (Input.GetMouseButtonDown(0))
//         {
//             if (api.IsHolding)
//             {
//                 // Release
//                 api.ClearPickedObject();
//             }
//             else
//             {
//                 // Try pick
//                 RaycastHit hit;
//                 if (Physics.Raycast(ray, out hit, pickRange, pickMask))
//                 {
//                     Debug.Log($"Hit: {hit.collider.gameObject.name}");
//                     api.SetPickedObject(hit.collider.gameObject);
//                 }
//             }
//         }
//     }
// }