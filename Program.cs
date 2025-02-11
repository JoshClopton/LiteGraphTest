


using GetSomeInput;

namespace LiteGraphTest
{
    using System.Collections.Specialized;
    using System.Net.Http.Headers;
    using LiteGraph.Sdk;
    using View.Sdk.Serialization;
    using View.Sdk.Semantic;
    using View.Sdk;

    /// <summary>
    /// Mock processing pipeline that simulates a file upload, type detection,
    /// and semantic cell extraction and uses LiteGraph for vector storage.
    /// </summary>
    public class Program
    {
        #region Public-Members

        #endregion
        
        #region Private-Members
        
        // Fields for creating tenant & graph
        private static Guid _TenantGuid = Guid.Empty;
        private static string _AccessKey = "";
        private static string _Endpoint = "http://192.168.197.128:8000/v1.0/tenants/00000000-0000-0000-0000-000000000000" +
                                          "/processing/semanticcell";
        private static Guid _GraphGuid = Guid.Parse("00000000-0000-0000-0000-000000000000");
        private static bool _EnableLogging = true;
        
        // The LiteGraph Sdk client that calls the remote LiteGraph.Server
        private static LiteGraphSdk _Sdk;
        private static View.Sdk.Serialization.Serializer _Serializer = new View.Sdk.Serialization.Serializer();
        private static View.Sdk.Semantic.ViewSemanticCellSdk _ViewSemanticCellSdk;
        
        #endregion

        #region Entrypoint

        /// <summary>
        /// Main.
        /// </summary>
        /// <param name="args">Arguments.</param>
        public static async Task Main(string[] args)
        {
            // EventWaitHandle waitHandle = new EventWaitHandle(false, EventResetMode.AutoReset);
            // bool waitHandleSignal = false;
            // do
            // {
            //     waitHandleSignal = waitHandle.WaitOne(1000);
            // }
            // while (!waitHandleSignal);
            
            // 1) Initialize the LiteGraphSdk, pointing to my LiteGraph.Server
            string endpointUrl = "http://localhost:8701";
            string bearerToken = "litegraphadmin";
            _Sdk = new LiteGraphSdk(endpointUrl, bearerToken);
            
            _ViewSemanticCellSdk = new ViewSemanticCellSdk(_TenantGuid, _AccessKey, _Endpoint
            );
            
            if (_EnableLogging) _ViewSemanticCellSdk.Logger
                += EmitLogMessage;
            

            // // 2) Create a tenant and graph.
            // Guid myTenantGuid = Guid.NewGuid();
            // var tenant = await _Sdk.CreateTenant(myTenantGuid, "Josh Test11");

            // Guid myGraphGuid = Guid.NewGuid();
            // var graph = await _Sdk.CreateGraph(tenant.GUID, myGraphGuid, "Josh's Test Graph11");
            //
            // // Store guids for later:
            // _TenantGuid = tenant.GUID;
            // _GraphGuid = graph.GUID;

            // 3) Get a file path from console
            Console.Write("Enter file path: ");
            string filePath = Console.ReadLine();
            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found!");
                return;
            }

            // 4) Read file as bytes for base64 encoding
            byte[] fileBytes = File.ReadAllBytes(filePath);
            Console.WriteLine($"Loaded file {filePath}, size {fileBytes.Length} bytes");

            // 5) Detect the file type
            TypeResult typeInfo = await DetectFileType(fileBytes);
            if (typeInfo == null)
            {
                Console.WriteLine("Failed to detect file type.");
                return;
            }
            Console.WriteLine($"Type Detection: {typeInfo.MimeType}, {typeInfo.Extension}, {typeInfo.Type}");
            
            await TestConnectivity();

            // 6) Call semantic cell service
            // var semanticCells = await GetSemanticCells(typeInfo.Type, fileBytes);
            // if (semanticCells == null)
            // {
            //     Console.WriteLine("Failed to get semantic cells.");
            //     return;
            // }
            // Console.WriteLine("Semantic cell extraction succeeded.");
            
            
            var mdRule = new MetadataRule
            {
                ProcessingEndpoint = "http://viewdemo:8000/",
                ProcessingAccessKey = "00000000-0000-0000-0000-000000000000",
                MinChunkContentLength = 1,
                MaxChunkContentLength = 512,
                ShiftSize = 512
            };
            
            var docTypeEnum = typeInfo.Type;
            
            View.Sdk.Semantic.SemanticCellResponse scr = await _ViewSemanticCellSdk.Process(docTypeEnum, mdRule, fileBytes);

            // 7) Store the entire semantic cell response as a single node in LiteGraph
            var documentNode = new LiteGraph.Sdk.Node
            {
                GUID = Guid.NewGuid(),
                TenantGUID = Guid.Parse("00000000-0000-0000-0000-000000000000"),
                GraphGUID = _GraphGuid,
                Name = Path.GetFileName(filePath),
                Labels = new List<string> { "document" },
                // ToDo: Is NameValueCollection the right type for tags?
                Tags = new NameValueCollection
                {
                    { "DocumentType", typeInfo.Type.ToString() },
                    { "Extension",    typeInfo.Extension },
                    {"NodeType", "Document" },
                    {"MimeType", typeInfo.MimeType },
                    {"FileName", Path.GetFileName(filePath) },
                    {"FilePath", filePath},
                    {"ContentLength", fileBytes.Length.ToString() }
                },
                Data = scr.SemanticCells
            };

            var createdDocNode = await _Sdk.CreateNode(Guid.Parse("00000000-0000-0000-0000-000000000000"), _GraphGuid, documentNode);
            if (createdDocNode == null)
            {
                Console.WriteLine("Failed to create document node in LiteGraph");
                return;
            }

            Console.WriteLine($"Document node created in LiteGraph with GUID {createdDocNode.GUID}");
            // If SemanticCells is not null and has atleast 1 item, create nodes for each cell and chunk
            if (scr.SemanticCells != null && scr.SemanticCells.Any())
            {
                foreach (var cell in scr.SemanticCells)
                {
                    // A) Create a node for this cell
                    var cellNode = new LiteGraph.Sdk.Node
                    {
                        GUID = Guid.NewGuid(),
                        TenantGUID = Guid.Parse("00000000-0000-0000-0000-000000000000"),
                        GraphGUID = _GraphGuid,
                        Name = $"Cell {cell.GUID}",
                        Labels = new List<string> { "semantic-cell" },
                        // Data = cell,
                        Tags = new NameValueCollection
                        {
                            { "CellType", cell.CellType.ToString() },
                            {"NodeType", "SemanticCell" },
                            { "MD5Hash", cell.MD5Hash },
                            { "SHA1Hash", cell.SHA1Hash },
                            { "SHA256Hash", cell.SHA256Hash },
                            { "Position", cell.Position.ToString() },
                            { "Length", cell.Length.ToString() }
                        }
                    };

                    var createdCellNode = await _Sdk.CreateNode(Guid.Parse("00000000-0000-0000-0000-000000000000"), _GraphGuid, cellNode);
                    if (createdCellNode == null)
                    {
                        Console.WriteLine($"Failed to create cell node for cell {cell.GUID}");
                        continue;
                    }

                    Console.WriteLine($"Cell node created with GUID {createdCellNode.GUID}");

                    // B) Create an edge from the Document node --> Cell node
                    var cellEdge = new Edge
                    {
                        GUID = Guid.NewGuid(),
                        TenantGUID = Guid.Parse("00000000-0000-0000-0000-000000000000"),
                        GraphGUID = _GraphGuid,
                        From = createdDocNode.GUID,
                        To = createdCellNode.GUID,
                        Name = $"Doc->Cell {cell.GUID}",
                        Labels = new List<string> { "edge", "document-cell" },
                        Tags = new NameValueCollection
                        {
                            { "Relationship", "ContainsCell" },
                            { "EdgeType", "DocumentToCell" },
                        }
                    };

                    var createdCellEdge = await _Sdk.CreateEdge(Guid.Parse("00000000-0000-0000-0000-000000000000"), _GraphGuid, cellEdge);
                    if (createdCellEdge != null)
                    {
                        Console.WriteLine($"Edge created from doc {createdDocNode.GUID} to cell {createdCellNode.GUID}");
                    }
                    else
                    {
                        Console.WriteLine($"Failed to create edge for cell {cell.GUID}");
                    }

                    // C) For each chunk in this cell, create a node, then link it to the cell node
                    if (cell.Chunks != null && cell.Chunks.Any())
                    {
                        foreach (var chunk in cell.Chunks)
                        {
                            // Create chunk node
                            var chunkNode = new LiteGraph.Sdk.Node
                            {
                                GUID = Guid.NewGuid(),
                                TenantGUID = Guid.Parse("00000000-0000-0000-0000-000000000000"),
                                GraphGUID = _GraphGuid,
                                Name = $"Chunk {chunk.GUID}",
                                Labels = new List<string> { "semantic-chunk" },
                                Data = chunk,
                                Tags = new NameValueCollection
                                {
                                    { "MD5Hash", chunk.MD5Hash },
                                    { "SHA1Hash", chunk.SHA1Hash },
                                    { "SHA256Hash", chunk.SHA256Hash },
                                    { "Position", chunk.Position.ToString() },
                                    { "Start", chunk.Start.ToString() },
                                    { "End", chunk.End.ToString() },
                                    { "Length", chunk.Length.ToString() },
                                    { "Content", chunk.Content },
                                    {"NodeType", "SemanticChunk" }
                                }
                            };

                            var createdChunkNode = await _Sdk.CreateNode(Guid.Parse("00000000-0000-0000-0000-000000000000"), _GraphGuid, chunkNode);
                            if (createdChunkNode == null)
                            {
                                Console.WriteLine($"Failed to create chunk node for chunk {chunk.GUID}");
                                continue;
                            }

                            Console.WriteLine($"Chunk node created with GUID {createdChunkNode.GUID}");

                            // Create edge from the cell node -> chunk node
                            var chunkEdge = new Edge
                            {
                                GUID = Guid.NewGuid(),
                                TenantGUID = Guid.Parse("00000000-0000-0000-0000-000000000000"),
                                GraphGUID = _GraphGuid,
                                From = createdCellNode.GUID,
                                To = createdChunkNode.GUID,
                                Name = $"Cell->Chunk {chunk.GUID}",
                                Labels = new List<string> { "edge", "cell-chunk" },
                                Tags = new NameValueCollection
                                {
                                    { "Relationship", "ContainsChunk" }, 
                                    { "EdgeType", "CellToChunk" },
                                    { "Position", chunk.Position.ToString() },
                                    { "Start", chunk.Start.ToString() },
                                    { "End", chunk.End.ToString() },
                                    { "Length", chunk.Length.ToString() },
                                    { "Content", chunk.Content }
                                }
                            };

                            var createdChunkEdge = await _Sdk.CreateEdge(Guid.Parse("00000000-0000-0000-0000-000000000000"), _GraphGuid, chunkEdge);
                            if (createdChunkEdge != null)
                            {
                                Console.WriteLine($"Edge created from cell {createdCellNode.GUID} to chunk {createdChunkNode.GUID}");
                            }
                            else
                            {
                                Console.WriteLine($"Failed to create edge for chunk {chunk.GUID}");
                            }
                        }
                    }
                }
            }
        }
        
        #endregion

        /// <summary>
        /// Call to type detection API.
        /// </summary>
        private static async Task<TypeResult> DetectFileType(byte[] fileBytes)
        {
            try
            {
                // Call the type detection API on my Ubuntu
                var url = "http://192.168.197.128:8000/v1.0/tenants/00000000-0000-0000-0000-000000000000/processing/typedetection";

                // Create an HttpClient (ToDo: Joel might have his own service here)
                using var client = new HttpClient();
        
                // Add the "Authorization: Bearer default" header
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "default");
        
                // Put the raw file bytes into the request body
                using var content = new ByteArrayContent(fileBytes);
                // Set the content type:
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        
                // Send the POST
                var response = await client.PostAsync(url, content);
                if (!response.IsSuccessStatusCode)
                    return null;

                // Parse the JSON response into the TypeDetectionResult
                // ToDo: Figure out where to grab the serializer from
                string json = await response.Content.ReadAsStringAsync();
                // var result = JsonConvert.DeserializeObject<TypeDetectionResult>(json);
                var result = _Serializer.DeserializeJson<TypeResult>(json);

                return result;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Call to semantic cell extraction API.
        /// base64-encode the file for the "Data" field in JSON.
        /// </summary>
        private static async Task<SemanticCellResponse> GetSemanticCells(string docType, byte[] fileBytes)
        {
            try
            {
                // Point to api on Ubuntu
                string url = "http://192.168.197.128:8000/v1.0/tenants/00000000-0000-0000-0000-000000000000/processing/semanticcell";
                // Create HttpClient
                using var client = new HttpClient();
        
                // Add the "Authorization: Bearer default" header
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "default");
                
                // Build request body (base64-encoded file)
                var requestBody = new
                {
                    DocumentType = docType,
                    MetadataRule = new
                    {
                        // ToDo: Is this where the port is specified on ubuntu?
                        SemanticCellEndpoint = "http://viewdemo:8000/",
                        MinChunkContentLength = 1,
                        MaxChunkContentLength = 512,
                        ShiftSize = 512
                    },
                    Data = Convert.ToBase64String(fileBytes)
                };

                // Serialize to JSON
                // Todo: Figure out where to get the serializer from
                // string reqJson = Newtonsoft.Json.JsonConvert.SerializeObject(requestBody);
                string reqJson = _Serializer.SerializeJson(requestBody);
                using var content = new StringContent(reqJson);
                // By default, StringContent sets Content-Type to "text/plain; charset=utf-8".
                // Had to override it to be "application/json" (with no explicit charset).
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                
                // Send the POST request
                var response = await client.PostAsync(url, content);
                if (!response.IsSuccessStatusCode) return null;

                // Deserialize the response JSON
                // ToDo: Figure out where to get the serializer from
                string json = await response.Content.ReadAsStringAsync();
                // var result = Newtonsoft.Json.JsonConvert.DeserializeObject<SemanticCellResponse>(json);
                var result = _Serializer.DeserializeJson<SemanticCellResponse>(json);
                return result;
            }
            catch
            {
                return null;
            }
        }
        private static async Task TestConnectivity()
        {
            Console.WriteLine("");
            Console.Write("State: ");

            if (await _ViewSemanticCellSdk.ValidateConnectivity())
                Console.WriteLine("Connected");
            else
                Console.WriteLine("Not connected");

            Console.WriteLine("");
        }
        
        private static void EmitLogMessage(View.Sdk.SeverityEnum sev, string msg)
        {
            if (!String.IsNullOrEmpty(msg)) Console.WriteLine(sev.ToString() + " " + msg);
        }
    }

    // JSON response shape from typedetection
    // public class TypeDetectionResult
    // {
    //     public string MimeType { get; set; }
    //     public string Extension { get; set; }
    //     public string Type { get; set; }
    // }

    // JSON response shape from semanticcell
    // public class SemanticCellResponse
    // {
    //     public bool Success { get; set; }
    //     public object Timestamp { get; set; }
    //     public List<SemanticCell> SemanticCells { get; set; }
    // }
    //
    // public class SemanticCell
    // {
    //     public string GUID { get; set; }
    //     public string CellType { get; set; }
    //     public string MD5Hash { get; set; }
    //     public string SHA1Hash { get; set; }
    //     public string SHA256Hash { get; set; }
    //     public int Position { get; set; }
    //     public int Length { get; set; }
    //     public List<Chunk> Chunks { get; set; }
    //     public List<SemanticCell> Children { get; set; }
    // }
    //
    // public class Chunk
    // {
    //     public string GUID { get; set; }
    //     public string MD5Hash { get; set; }
    //     public string SHA1Hash { get; set; }
    //     public string SHA256Hash { get; set; }
    //     public int Position { get; set; }
    //     public int Start { get; set; }
    //     public int End { get; set; }
    //     public int Length { get; set; }
    //     public string Content { get; set; }
    //     
    //     public List<float> Embeddings { get; set; }
    // }
}