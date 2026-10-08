using UnityEngine;

namespace FactoryLab
{
    public sealed class FactoryStationView : MonoBehaviour
    {
        public int stationIndex;
        public GameObject selection;
        public Transform[] inputSlots;
        public Transform outputSlot, workSlot;
        public Renderer statusLamp;
    }
}
