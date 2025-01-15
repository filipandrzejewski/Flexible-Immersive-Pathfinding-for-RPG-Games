using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

public class NavMeshLinksGenerator : MonoBehaviour
{
    [Header("Link Prefabs")]
    [SerializeField, Tooltip("[AUTO-ASSIGN: LinkPrefab(Standard)]\n Prefab used for NavMesh links between edges")] 
    public Transform standardLinkPrefab;
    [SerializeField, Tooltip("[AUTO-ASSIGN: LinkPrefab(Wide)]\n Prefab used for NavMesh links stratching down from ledges")] 
    public Transform dropDownLinkPrefab;

    [Header("Link Raycasts")]
    [SerializeField, Tooltip("If enabled performs customized box cast with the dimensions dependent on agent size and height of the jump arcs")]
    public bool enableJumpArcRayCasts;
    [SerializeField, Tooltip("Determines the stylized height of the jump arc. Is used only when jumpArcRaycast is enabled")]
    public float maxjumpArcHeight = 1.2f;

    [Header("Manager Object References")]
    [SerializeField, Tooltip("[AUTO-ASSIGN: First scene object containing NavMeshSurface component]\n Main NavMeshSurface for the scene")]  // this may need to be extended to be used on many different nav mesh surfaces each for differentr agent types
    public NavMeshSurface navMeshSurface;
    [SerializeField, Tooltip("[AUTO-ASSIGN: NavLinkManager attached to this object]\n Manager component used for organizing autogenerated NavMeshLinks")] 
    private NavLinkManager navLinkManager;

    [Header("Edge Detection Settings")]
    [SerializeField, Tooltip("Edges longer than specified value will be divided into 2 edges.")] 
    public float maxEdgeLength = 8f;
    [SerializeField, Tooltip("Edges shorter than specified value will be averaged and grouped based on their neighbours.\n When set to 0 this parameter will be ignored")] 
    public float minEdgeLength = 0.2f; 
    [SerializeField, Tooltip("Maximal size of a group of small edges to be averaged into one Edge")] 
    public int maxGroupSize = 6;
    [SerializeField, Tooltip("Minimal size of a group of small edges to be averaged into one Edge")] 
    public int minGroupSize = 3;

    [SerializeField] [HideInInspector]
    private EdgeParameters edgeParameters;


    [Header("Basic Link Generation Settings")]
    [SerializeField, Tooltip("Maximum distance for conections edge to edge\n When set to 0 this parameter will be ignored")] 
    public float maxEdgeLinkDistance = 16;
    [SerializeField, Tooltip("Distance at which links will be created with less restrictions")] 
    public float shortLinkDistance = 2;
    [SerializeField, Tooltip("Maximum distance to search for dropdown links")] 
    float maxDropDownLinkDistance = 20f;

    [Header("Advanced Link Generation Settings")]
    [SerializeField, Tooltip("Determines the angle at which to search for the dropdown links (is a Y component in raycast vector direction)")]
    public float dropDownSteepnessModifier = 3;
    [SerializeField, Tooltip("Angles (rotations in Y axis) at which to search for dropdown links")]
    public float[] dropDownLinkAngles = { 0f, -30f, 30f };
    [SerializeField, Tooltip("Angle limit at which standard links are permitted to connect relative to Forward Direction.\n Forward Direction is  link's FalloffDirection projected onto a XZ plane")]
    public float standardAngleRestrictionForward = 65;
    [SerializeField, Tooltip("Angle limit at which standard links are permitted to connect relative to Upward Direction.\n Upward Direction is normal to a surface an edge is a part of")]
    public float standardAngleRestrictionUpward = 30;
    [SerializeField, Tooltip("Angle limit at which short links are permitted to connect relative to Forward Direction.\n Forward Direction is  link's FalloffDirection projected onto a XZ plane")]
    public float permissiveAngleRestrictionForward = 89;
    [SerializeField, Tooltip("Angle limit at which standard links are permitted to connect relative to Upward Direction.\n Upward Direction is normal to a surface an edge is a part of")]
    public float permissiveAngleRestrictionUpward = 65;

    [Space]

    [SerializeField] private Transform debugFaloffPointPrefab;
    [SerializeField] private Transform debugCornerPointPrefab;

    [Space]

    [SerializeField] [HideInInspector] private NavMeshBuildSettings navMeshSettings; //

    [SerializeField] [HideInInspector] private Transform generatedLinksGroup;
    [SerializeField] [HideInInspector] private List<Edge> edges = new List<Edge>();
    [SerializeField] [HideInInspector] private Vector3[] vertices; // every vertice of the nav mesh
    [SerializeField] [HideInInspector] private int[] pairIndices; // connections between vertices paired by index in the vertices table

    
    
    

    //private void Start()
    //{
    //    if (edges.Count == 0) { FindEdges(); }
    //    if (generatedLinksGroup == null) { generatedLinksGroup = new GameObject("AllLinksGroup").transform; }
        
    //}

    public bool AutoAssign()
    {
#if UNITY_EDITOR
        bool allComponentsAssigned = true;
        if (standardLinkPrefab == null)
        {
            string linkPrefabPath = FindAssetPathByName("LinkPrefab(Standard)", "t:Prefab");
            if (!string.IsNullOrEmpty(linkPrefabPath))
            {
                standardLinkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(linkPrefabPath).transform;
            }
            else
            {
                Debug.LogWarning($"Prefab with name 'LinkPrefab(Standard)' not found in Assets.");
                allComponentsAssigned = false;
            }
        }

        if (dropDownLinkPrefab == null)
        {
            string linkPrefabPath = FindAssetPathByName("LinkPrefab(Wide)", "t:Prefab");
            if (!string.IsNullOrEmpty(linkPrefabPath))
            {
                standardLinkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(linkPrefabPath).transform;
            }
            else
            {
                Debug.LogError($"Prefab with name 'LinkPrefab(Wide)' not found in Assets.");
                allComponentsAssigned = false;
            }
        }

        if (navMeshSurface == null)
        {
            GameObject[] allObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (GameObject obj in allObjects)
            {
                if (obj.GetComponent<NavMeshSurface>() != null)
                {
                    navMeshSurface = obj.GetComponent<NavMeshSurface>();
                    break;
                }
            }
            if (navMeshSurface == null)
            {
                Debug.LogError($"No NavMeshSurface detected in the current scene. Make sure a NavMeshSurface component is present and the mesh is baked before generating links");
                allComponentsAssigned = false;
            }
        }

        if (navLinkManager == null)
        {
            navLinkManager = GetComponent<NavLinkManager>();
            if (navLinkManager == null)
            {
                Debug.LogError($"No NavLinkManager component detected on the object: {this.name}. Make sure the manager object holds both NavLinkGenerator and NavLinkManager scripts");
                allComponentsAssigned = false;
            }
        }
        EditorUtility.SetDirty(this);
        return (allComponentsAssigned);
#endif
    }

#if UNITY_EDITOR
    private string FindAssetPathByName(string assetName, string filter)
    {
        string[] guids = AssetDatabase.FindAssets($"{assetName} {filter}");
        if (guids.Length > 0)
        {
            return AssetDatabase.GUIDToAssetPath(guids[0]);
        }
        return null;
    }
#endif

    private void GetCurrentNavMeshSettings()
    {
        navMeshSettings = NavMesh.GetSettingsByID(navMeshSurface.agentTypeID);
    }

    private void FindEdges()
    {
        NavMeshTriangulation meshData = NavMesh.CalculateTriangulation();

        vertices = meshData.vertices;
        pairIndices = meshData.indices;

        for (int i = 0; i < pairIndices.Length - 1; i += 3)
        {
            // If a triangle has its edge repeated in another triangle the connection should be deleted as it is not actually the Edge of the solid
            PairToEdge(i, i + 1, i + 2);
            PairToEdge(i + 1, i + 2, i);
            PairToEdge(i + 2, i, i + 1);
        }

        List<List<Edge>> groupedEdges = GroupShortEdges();
        foreach (List<Edge> group in groupedEdges)
        {
            Edge mergedEdge = GroupRepresentative(group);

            // Remove grouped edges
            foreach (Edge edge in group)
            {
                edges.Remove(edge);
            }

            // Add the merged edge
            edges.Add(mergedEdge);
        }

        foreach (Edge edge in edges)
        {
            if (edge.hasPivotPoint)
            {
                foreach (Vector3 falloffPoint in edge.falloffPoint)
                {
                    Debug.Log("Debug: Valid Edge");
                    //Instantiate(debugFaloffPointPrefab, falloffPoint, Quaternion.identity);
                }
            }
            else
            {
                Debug.Log("Debug: Invalid Edge");
                //Instantiate(debugCornerPointPrefab, edge.start, Quaternion.identity);
                //Instantiate(debugCornerPointPrefab, edge.end, Quaternion.identity);
            }
        }

        edges.RemoveAll(edge => !edge.hasPivotPoint);
    }

    private void PairToEdge(int n1, int n2, int n3) //N1 and N2 will be used for calculating the edge, N3 will be used to calculate the norlmal of the plane that the edge is connected to  
    {
        Vector3 point1 = vertices[pairIndices[n1]];
        Vector3 point2 = vertices[pairIndices[n2]];
        Vector3 point3 = vertices[pairIndices[n3]];
        Vector3 surfaceVector = point3 - point1;

        Vector3 surfaceNormal = Vector3.Cross(point2 - point1, surfaceVector).normalized;


        Edge newEdge = new Edge(point1, point2, surfaceNormal, navMeshSettings);

        //remove duplicate connection as they are not edges
        foreach (Edge edge in edges)
        {
            if ((edge.start == point1 & edge.end == point2) || (edge.start == point2 & edge.end == point1))
            {
                edges.Remove(edge);
                return;
            }
        }

        edges.Add(newEdge);
    }

    private List<List<Edge>> GroupShortEdges()
    {
        List<Edge> shortEdges = edges.FindAll(edge => edge.length < minEdgeLength);
        List<List<Edge>> groupedEdges = new List<List<Edge>>();
        HashSet<Edge> visited = new HashSet<Edge>();

        foreach (Edge edge in shortEdges)
        {
            if (visited.Contains(edge)) continue;

            List<Edge> group = new List<Edge>();
            Stack<Edge> stack = new Stack<Edge>();
            stack.Push(edge);

            while (stack.Count > 0 && group.Count < maxGroupSize)
            {
                Edge current = stack.Pop();
                if (visited.Contains(current)) continue;

                visited.Add(current);
                group.Add(current);

                // Find neighboring short edges sharing a start or end point
                foreach (Edge neighbor in shortEdges)
                {
                    if (visited.Contains(neighbor)) continue;

                    if (current.start == neighbor.start || current.start == neighbor.end ||
                        current.end == neighbor.start || current.end == neighbor.end)
                    {
                        stack.Push(neighbor);
                    }
                }
            }

            if (group.Count > minGroupSize - 1)
            {
                groupedEdges.Add(group);
            }
        }

        return groupedEdges;
    }

    private Edge MergeEdgeGroup(List<Edge> group)
    {
        Vector3 averageStart = Vector3.zero;
        Vector3 averageEnd = Vector3.zero;
        Vector3 averageNormal = Vector3.zero;
        float totalLength = 0f;

        foreach (Edge edge in group)
        {
            averageStart += edge.start;
            averageEnd += edge.end;
            averageNormal += edge.edgeSurfaceNormal;
            totalLength += edge.length;
        }

        averageStart /= group.Count;
        averageEnd /= group.Count;
        averageNormal = averageNormal.normalized;

        Edge mergedEdge = new Edge(averageStart, averageEnd, averageNormal, navMeshSettings);
        return mergedEdge;
    }

    private Edge GroupRepresentative(List<Edge> group)
    {
        Vector3 averagefalloffDirection = Vector3.zero;
        foreach (var edge in group)
        {
            averagefalloffDirection += edge.falloffDirection; 
        }
        averagefalloffDirection.Normalize();

        Edge bestRepresentative = null;
        float smallestAngle = float.MaxValue;

        foreach (var edge in group)
        {
            float angle = Vector3.Angle(averagefalloffDirection, edge.falloffDirection);
            if (angle < smallestAngle)
            {
                smallestAngle = angle;
                bestRepresentative = edge;
            }
        }

        Edge newEdge = null;

        if (bestRepresentative != null)
        {
            newEdge = new Edge(bestRepresentative);
            newEdge.falloffDirection = (newEdge.falloffDirection.normalized + averagefalloffDirection).normalized;
        }

        return newEdge;

    }


    private void CheckCreateConditions()
    {
        GetCurrentNavMeshSettings();
        if (edges.Count == 0) { FindEdges(); }
        else
        {
            if (edgeParameters != null)
            {
                if (edgeParameters.ParametersChanged(maxEdgeLength, minEdgeLength, maxGroupSize, minGroupSize))
                {
                    edges.Clear();
                    FindEdges();
                    return;
                }
            }
            
            if (VerticesChanged())
            {
                edges.Clear();
                FindEdges();
                return;
            }
        }
    }

    private bool VerticesChanged()
    {
        Vector3[] currentVertices = NavMesh.CalculateTriangulation().vertices;

        if (vertices == null || currentVertices.Length != vertices.Length)
        {
            return true;
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i] != currentVertices[i])
            {
                return true;
            }
        }

        return false;
    }

    public void CreateLinks()
    {
        
        CheckCreateConditions();

        edgeParameters = new EdgeParameters(maxEdgeLength, minEdgeLength, maxGroupSize, minGroupSize);

        if (edges.Count == 0)
        {
            Debug.LogWarning("Links could not be made as there were no suitable Edges detected in the scene. Check the NavMeshSUrface setting");
            return;
        }
        if (generatedLinksGroup == null) { generatedLinksGroup = new GameObject("AllLinksGroup").transform; }
        if (standardLinkPrefab == null) { return; }

        float progress = 0;
        try
        {
            foreach (Edge edge in edges)
            {
                foreach (Edge targetEdge in edges)
                {
                    if (edge == targetEdge) { continue; }

                    int startPointIndex = 0;
                    int endPointIndex = 0;

                    if (LinkExists(edge.connectionPoint[startPointIndex], targetEdge.connectionPoint[endPointIndex])) { continue; }

                    EditorUtility.DisplayProgressBar(
                        "Generating Links...",
                        $"Checking connection {progress + 1} of {edges.Count - 1}",
                        progress);

                    if (ValidConnectionExists(edge, targetEdge, out startPointIndex, out endPointIndex))
                    {
                        Transform linkObject = Instantiate(standardLinkPrefab.transform, edge.connectionPoint[startPointIndex], Quaternion.identity); // prev: Quaternion.LookRotation(direction) apparently rotation of link does not matter at all?
                        var link = linkObject.GetComponent<NavMeshLink>();



                        Vector3 globalEndPoint = targetEdge.connectionPoint[endPointIndex];
                        Vector3 localEndPoint = linkObject.InverseTransformPoint(globalEndPoint);

                        navLinkManager.navLinks.Add(new 
                            (edge.connectionPoint[startPointIndex], globalEndPoint, link, true));

                        link.endPoint = localEndPoint;
                        link.UpdateLink();
                        linkObject.transform.SetParent(generatedLinksGroup);
                    }
                }

                AddDropDownLink(edge);

                progress += 1;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    public bool LinkExists(Vector3 startPoint, Vector3 endPoint)
    {
        return navLinkManager.navLinks.Any(link =>
            (link.start == startPoint && link.end == endPoint) ||
            (link.start == endPoint && link.end == startPoint)
        );
    }

    public void AddDropDownLink(Edge edge)
    {
        for (int i = 0; i < 3; i++)
        {
            Quaternion rotation = Quaternion.Euler(0, dropDownLinkAngles[i], 0);
            Vector3 checkDirection = rotation * edge.falloffDirection.normalized; // Spread direction based on falloff

            //Debug.DrawLine(edge.falloffPoint[0], edge.falloffPoint[0] + (2 * checkDirection), Color.green, 5f);
            //Debug.DrawLine(edge.falloffPoint[0], edge.falloffPoint[0] + (2 * (checkDirection + (dropDownSteepnessModifier * Vector3.down))), Color.yellow, 5f);


            if (Physics.Raycast(edge.falloffPoint[0], checkDirection + (dropDownSteepnessModifier * Vector3.down), out RaycastHit hit, maxDropDownLinkDistance))
            {
                if (LinkExists(edge.connectionPoint[0], hit.point)) { return; }

                Vector3 startPoint = edge.connectionPoint[0];
                Vector3 endPoint = hit.point;

                Transform linkObject = Instantiate(dropDownLinkPrefab.transform, startPoint, Quaternion.identity);
                var link = linkObject.GetComponent<NavMeshLink>();

                Vector3 localEndPoint = linkObject.InverseTransformPoint(endPoint);

                navLinkManager.navLinks.Add(new LinkData(startPoint, endPoint, link, true));

                link.endPoint = localEndPoint;
                link.UpdateLink();
                linkObject.transform.SetParent(generatedLinksGroup);

                return;

                //Debug.DrawLine(startPoint, endPoint, Color.green, 1f); // Debug successful link
            }
            else
            {
                //Debug.DrawRay(rayOrigin, (rayDirection + Vector3.down) * rayDistance, Color.red, 1f); // Debug failed ray
            }
        }
    }

    public bool ValidConnectionExists(Edge edge, Edge targetEdge, out int beginIndex, out int endIndex)
    {
        for (int i = 0; i < edge.falloffPoint.Count; i ++)
        {
            for (int j = 0; j < targetEdge.falloffPoint.Count; j++)
            {
                Vector3 direction = (targetEdge.falloffPoint[j] - edge.falloffPoint[i]).normalized;
                float distance = Vector3.Distance(targetEdge.falloffPoint[j], edge.falloffPoint[i]);

                

                if (maxEdgeLinkDistance > 0 & distance > maxEdgeLinkDistance) { continue; } // skip connections that are physically too long | 0 -> maxLinkDistance ignored
                if (shortLinkDistance > 0 & distance < shortLinkDistance) // loosen the angle restrictions on very short links | 0 -> shortLinkDistance ignored
                {
                    // Permisive angles:

                    if (Vector3.Angle(direction, edge.edgeSurfaceNormal) > permissiveAngleRestrictionUpward &
                    Vector3.Angle(direction, Vector3.ProjectOnPlane(edge.falloffDirection, Vector3.up)) > permissiveAngleRestrictionForward) { continue; } 

                    if (Vector3.Angle(-direction, targetEdge.edgeSurfaceNormal) > permissiveAngleRestrictionUpward &
                        Vector3.Angle(-direction, Vector3.ProjectOnPlane(targetEdge.falloffDirection, Vector3.up)) > permissiveAngleRestrictionForward) { continue; } 
                }
                else
                {
                    //Strict angles:

                    if (Vector3.Angle(direction, edge.edgeSurfaceNormal) > standardAngleRestrictionUpward &
                    Vector3.Angle(direction, Vector3.ProjectOnPlane(edge.falloffDirection, Vector3.up)) > standardAngleRestrictionForward) { continue; } //skip sharp connections with selected edge (both regarding to edge surface normal and falloff direction flattened in regards to the floor)


                    if (Vector3.Angle(-direction, targetEdge.edgeSurfaceNormal) > standardAngleRestrictionUpward &
                        Vector3.Angle(-direction, Vector3.ProjectOnPlane(targetEdge.falloffDirection, Vector3.up)) > standardAngleRestrictionForward) { continue; } //skip same sharp connections with target edge 
                }

                if (enableJumpArcRayCasts) // make an additionall customized box raycast to determine if the middle of the path is open enough for a jump arc
                {
                    float linkArcHeight = navMeshSettings.agentHeight + (maxjumpArcHeight * distance / maxEdgeLinkDistance);

                    float dot = Vector3.Dot(new Vector3(direction.x, Mathf.Abs(direction.y), direction.z), Vector3.up);
                    //abs from height value means the dot product will result in value between 1 (perfectly alligned with upwards or downwards direction) and 0 (perfectly perpendicular to these directions)
                    // I reverse the values because I want 1 - full range if the direcion if perfectly perpendicular to up) and 0 when the direction is perfectly alligned with up vector
                    float steepnessModifier = 1 - Vector3.Dot(new Vector3(direction.x, Mathf.Abs(direction.y), direction.z), Vector3.up);

                    linkArcHeight *= steepnessModifier;

                    
                    if (Physics.BoxCast(
                        edge.falloffPoint[i] + direction * distance * 0.1f + Vector3.up * linkArcHeight * 0.5f,
                        new Vector3(navMeshSettings.agentRadius, linkArcHeight * 0.5f, navMeshSettings.agentRadius),
                        direction,
                        Quaternion.identity,
                        distance * 0.8f) &&
                        Physics.BoxCast(
                        targetEdge.falloffPoint[i] - direction * distance * 0.1f + Vector3.up * linkArcHeight * 0.5f,
                        new Vector3(navMeshSettings.agentRadius, linkArcHeight * 0.5f, navMeshSettings.agentRadius),
                        -direction,
                        Quaternion.identity,
                        distance * 0.8f))
                    {
                        //Debug.DrawLine(edge.falloffPoint[i], targetEdge.falloffPoint[j], Color.red, 5.0f);

                        beginIndex = i;
                        endIndex = j;

                        return false;
                    }
                }

                if (!Physics.Raycast(edge.falloffPoint[i], direction, distance) &&
                        !Physics.Raycast(targetEdge.falloffPoint[j], -direction, distance)) // no collisions detected on a way between falloff points both ways
                {
                    //Debug.DrawLine(edge.falloffPoint[i], targetEdge.falloffPoint[j], Color.green, 5.0f);

                    beginIndex = i;
                    endIndex = j;

                    return true;
                }
                else
                {
                    //Debug.DrawLine(edge.falloffPoint[i], targetEdge.falloffPoint[j], Color.red, 5.0f);
                }




            }
        }
        beginIndex = 0;
        endIndex = 0;
        return false;
        
    }

    public int HighlightEdgeDirections()
    {
        int numberOfHighlightedEdges = 0;
        foreach (Edge edge in edges)
        {
            foreach (Vector3 connectionPoint in edge.connectionPoint)
            {
                numberOfHighlightedEdges += 1;

                Vector3 start = connectionPoint;
                Vector3 end = connectionPoint + edge.falloffDirection * 2;
                Debug.DrawLine(start, end, Color.blue, 5f);

                //Vector3 start2 = connectionPoint;
                //Vector3 end2 = connectionPoint + Vector3.Cross(edge.falloffDirection, Vector3.Cross(edge.falloffDirection, Vector3.up)) * 2;
                //Debug.DrawLine(start2, end2, Color.yellow, 5f);
            }
        }
        return numberOfHighlightedEdges;
    }

    public int HighlightLinkDirections()
    {
        int numberOfHighlightedLinks = 0;
        return numberOfHighlightedLinks;

    }

    public void HighlightAll()
    {
        Debug.Log($"Highlighted {HighlightEdgeDirections()} valid Edge connections and their directions (Blue).");


    }

    public void DeleteLinks()
    {
        for (int i = navLinkManager.navLinks.Count - 1; i >= 0; i--)
        {
            LinkData link = navLinkManager.navLinks[i];

            if (!link.wasGenerated) { continue; }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (link.linkComponent != null)
                {
                    navLinkManager.DeleteLink(link.linkComponent);
                    DestroyImmediate(link.linkComponent.gameObject);
                }
            }
            else
            {
                if (link.linkComponent != null)
                {
                    Destroy(link.linkComponent.gameObject);
                }
            }
#else
            if (link.linkComponent != null)
            {
                Destroy(link.linkComponent.gameObject);
            }
#endif
        }

#if UNITY_EDITOR
        EditorUtility.SetDirty(navLinkManager);
#endif
    }
}

public class EdgeParameters
{
    public float maxEdgeLength;
    public float minEdgeLength;
    public int maxGroupSize;
    public int minGroupSize;

    public EdgeParameters(float maxLength, float minLength, int maxSize, int minSize)
    {
        maxEdgeLength = maxLength;
        minEdgeLength = minLength;
        maxGroupSize = maxSize;
        minGroupSize = minSize;
    }

    public bool ParametersChanged(float maxLength, float minLength, int maxSize, int minSize)
    {
        bool changed = Compare(maxLength, minLength, maxSize, minSize);
        if (changed)
        {
            maxEdgeLength = maxLength;
            minEdgeLength = minLength;
            maxGroupSize = maxSize;
            minGroupSize = minSize;
        }
        return changed;

    }

    private bool Compare(float maxLength, float minLength, int maxSize, int minSize)
    {
        if (maxLength != maxEdgeLength) { return true; }
        if (minLength != minEdgeLength) { return true; }
        if (maxSize != maxGroupSize) { return true; }
        if (minSize != minGroupSize) { return true; }

        return false;
    }
}



//[CustomEditor(typeof(NavMeshLinksGenerator))]
//public class EdgeManagerEditor : Editor
//{
//    public override void OnInspectorGUI()
//    {
//        DrawDefaultInspector();

//        NavMeshLinksGenerator navMeshLinks = (NavMeshLinksGenerator)target;

//        if (GUILayout.Button("AutoAssign"))
//        {
//            navMeshLinks.AutoAssign();
//        }
//    }
//}
