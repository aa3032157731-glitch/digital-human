using UnityEngine;

namespace FactoryLab
{
    public static class FactoryGeometry
    {
        public static readonly Color Navy = new Color(.075f, .13f, .19f);
        public static readonly Color Teal = new Color(.08f, .67f, .64f);
        public static GameObject Box(Transform parent, string name, Vector3 position, Vector3 scale, Material material, PrimitiveType shape = PrimitiveType.Cube)
        {
            var obj = GameObject.CreatePrimitive(shape); obj.name = name; obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position; obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }
        public static Transform Empty(Transform parent, string name, Vector3 position)
        {
            var obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.transform.localPosition = position; return obj.transform;
        }
        public static TextMesh Label(Transform parent, string text, Vector3 position, float size, Color color)
        {
            var obj = Empty(parent, "Label " + text, position);
            var label = obj.gameObject.AddComponent<TextMesh>(); label.text = text; label.characterSize = size;
            label.fontSize = 64; label.anchor = TextAnchor.MiddleCenter; label.alignment = TextAlignment.Center; label.color = color;
            obj.localRotation = Quaternion.Euler(65, 0, 0);
            return label;
        }

        // Palette: floor, white, metal, teal, amber, dark, cardboard.
        public static FactoryStationView[] Build(Transform parent, FactoryConfiguration config, Material[] m)
        {
            Box(parent, "Factory foundation", new Vector3(6, -.2f, 4), new Vector3(12.5f, .35f, 8.5f), m[5]);
            Box(parent, "Floor 12m x 8m", new Vector3(6, -.015f, 4), new Vector3(12, .06f, 8), m[0]);
            Box(parent, "Back wall", new Vector3(6, .9f, 8.05f), new Vector3(12.2f, 1.8f, .12f), m[1]);
            Box(parent, "Left wall", new Vector3(-.05f, .65f, 4), new Vector3(.12f, 1.3f, 8), m[1]);
            Box(parent, "Back trim", new Vector3(6, 1.76f, 8), new Vector3(12, .1f, .13f), m[3]);
            Box(parent, "Aisle", new Vector3(6, .026f, 4), new Vector3(12, .018f, 2), m[2]);
            for (int i = 0; i < 12; i++)
            {
                Box(parent, "Aisle edge upper", new Vector3(i + .4f, .04f, 3.05f), new Vector3(.58f, .016f, .045f), m[4]);
                Box(parent, "Aisle edge lower", new Vector3(i + .4f, .04f, 4.95f), new Vector3(.58f, .016f, .045f), m[4]);
            }
            Label(parent, "FACTORY LAB  /  PROCESS STUDY", new Vector3(6, 1.3f, 7.88f), .055f, Navy).transform.localRotation = Quaternion.identity;
            var views = new FactoryStationView[6];
            string[] names = { "MATERIALS", "ASSEMBLY", "FASTENING", "INSPECTION", "PACKING", "FINISHED" };
            for (int s = 0; s < 6; s++)
            {
                var center = config.Centers[s]; var root = Empty(parent, config.Stations[s].Id, new Vector3((float)center.x, 0, (float)center.z));
                var view = root.gameObject.AddComponent<FactoryStationView>(); view.stationIndex = s; views[s] = view;
                var select = Empty(root, "Selection", new Vector3(0, .052f, 0));
                Box(select, "North", new Vector3(0, 0, .82f), new Vector3(3.35f, .025f, .045f), m[3]);
                Box(select, "South", new Vector3(0, 0, -.82f), new Vector3(3.35f, .025f, .045f), m[3]);
                Box(select, "East", new Vector3(1.66f, 0, 0), new Vector3(.045f, .025f, 1.66f), m[3]);
                Box(select, "West", new Vector3(-1.66f, 0, 0), new Vector3(.045f, .025f, 1.66f), m[3]);
                view.selection = select.gameObject; view.selection.SetActive(false);
                if (s == 0 || s == 5)
                {
                    for (int y = 0; y < 3; y++) Box(root, "Shelf", new Vector3(0, .2f + y * .48f, 0), new Vector3(2.1f, .07f, .72f), m[2]);
                    foreach (float x in new[] { -.96f, .96f })
                        foreach (float z in new[] { -.3f, .3f }) Box(root, "Upright", new Vector3(x, .82f, z), new Vector3(.07f, 1.6f, .07f), m[5]);
                }
                else
                {
                    Box(root, "Worktop", new Vector3(0, .85f, 0), new Vector3(1.65f, .1f, .85f), m[1]);
                    foreach (float x in new[] { -.69f, .69f })
                        foreach (float z in new[] { -.3f, .3f }) Box(root, "Leg", new Vector3(x, .4f, z), new Vector3(.07f, .8f, .07f), m[5]);
                    Box(root, "Tool tray", new Vector3(-.47f, .94f, 0), new Vector3(.4f, .1f, .36f), m[3]);
                    if (s == 2)
                    {
                        Box(root, "Driver handle", new Vector3(.5f, 1.08f, 0), new Vector3(.1f, .32f, .12f), m[3]);
                        Box(root, "Driver shaft", new Vector3(.5f, 1.28f, 0), new Vector3(.025f, .18f, .025f), m[2]);
                    }
                    if (s == 3)
                    {
                        Box(root, "Test fixture", new Vector3(.5f, 1.06f, .18f), new Vector3(.4f, .28f, .1f), m[5]);
                        Box(root, "Test display", new Vector3(.5f, 1.08f, .12f), new Vector3(.31f, .16f, .025f), m[3]);
                    }
                    if (s == 4) Box(root, "Packing stock", new Vector3(.55f, 1.01f, 0), new Vector3(.35f, .2f, .4f), m[6]);
                }
                view.statusLamp = Box(root, "Status beacon", new Vector3(.9f, 1.6f, 0), new Vector3(.13f, .13f, .13f), m[3], PrimitiveType.Sphere).GetComponent<Renderer>();
                Label(root, (s + 1).ToString("00") + "  " + names[s], new Vector3(0, .09f, center.z < 4 ? -.77f : .78f), .042f, Navy);
                var slots = new Transform[4];
                for (int i = 0; i < 4; i++)
                {
                    float z = -.46f + i * .31f;
                    Box(root, "Input pad " + i, new Vector3(-1.25f, .09f, z), new Vector3(.45f, .12f, .27f), m[2]);
                    slots[i] = Empty(root, "Input " + i, new Vector3(-1.25f, .21f, z));
                }
                view.inputSlots = slots;
                Box(root, "Output pad", new Vector3(1.25f, .09f, 0), new Vector3(.45f, .12f, .45f), m[3]);
                view.outputSlot = Empty(root, "Output", new Vector3(1.25f, .2f, 0));
                view.workSlot = Empty(root, "Working product", new Vector3(.07f, .92f, 0));
                var port = config.Ports[s];
                Box(parent, "Handoff " + s, new Vector3((float)port.x, .055f, (float)port.z), new Vector3(.65f, .025f, .38f), m[3]);
            }
            return views;
        }

        public static Transform CreateCarrier(Transform parent, Material[] m)
        {
            var cart = Empty(parent, "Carrier", Vector3.zero);
            Box(cart, "Deck", new Vector3(0, .22f, 0), new Vector3(.64f, .12f, .7f), m[3]);
            foreach (float x in new[] { -.24f, .24f })
                foreach (float z in new[] { -.24f, .24f }) Box(cart, "Wheel", new Vector3(x, .1f, z), new Vector3(.15f, .16f, .13f), m[5], PrimitiveType.Sphere);
            Box(cart, "Handle", new Vector3(0, .6f, -.28f), new Vector3(.6f, .05f, .045f), m[2]);
            foreach (float x in new[] { -.27f, .27f }) Box(cart, "Handle post", new Vector3(x, .43f, -.28f), new Vector3(.04f, .35f, .04f), m[2]);
            return cart;
        }

        public static Transform CreateProduct(Transform parent, string name, Material[] m)
        {
            var product = Empty(parent, name, Vector3.zero);
            var raw = Empty(product, "Kit", Vector3.zero);
            Box(raw, "Tray", new Vector3(0, .035f, 0), new Vector3(.32f, .06f, .24f), m[4]);
            Box(raw, "Parts", new Vector3(.02f, .11f, 0), new Vector3(.17f, .09f, .14f), m[1]);
            var lamp = Empty(product, "Lamp", Vector3.zero);
            Box(lamp, "Base", new Vector3(0, .03f, 0), new Vector3(.25f, .045f, .23f), m[1], PrimitiveType.Cylinder);
            Box(lamp, "Stem", new Vector3(0, .19f, 0), new Vector3(.035f, .32f, .035f), m[2]);
            Box(lamp, "Head", new Vector3(.06f, .36f, 0), new Vector3(.22f, .1f, .15f), m[3]);
            var packed = Empty(product, "Package", Vector3.zero);
            Box(packed, "Box", new Vector3(0, .14f, 0), new Vector3(.31f, .28f, .27f), m[6]);
            Box(packed, "Tape", new Vector3(0, .285f, 0), new Vector3(.055f, .01f, .28f), m[1]);
            return product;
        }
    }
}
