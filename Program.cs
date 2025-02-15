namespace LiteGraphTest
{
    using ConsoleTables;
    using GetSomeInput;
    using LiteGraph.Sdk;
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using View.Sdk;
    using View.Sdk.Embeddings;
    using View.Sdk.Processor;
    using View.Sdk.Semantic;
    
    /// <summary>
    /// LiteGraph Test Program. 
    /// </summary>
    public class Program
    {
        #region Public-Members
        
        #endregion
        
        #region Private-Members
        
        private static readonly string _LitegraphEndpoint = "http://localhost:8701";
        private static readonly string _LitegraphBearerToken = "litegraphadmin";
        private static Guid _Graph = Guid.Parse("00000000-0000-0000-0000-000000000000");
        private static Guid _Tenant = Guid.Parse("00000000-0000-0000-0000-000000000000");
        private static string _AccessKey = "default";
        private static string _ViewEndpoint = "http://localhost:8000/";
        private static readonly bool _EnableLogging = true;
        private static View.Sdk.Serialization.Serializer _Serializer = new View.Sdk.Serialization.Serializer();
        private static VectorSearchTypeEnum _SearchType = VectorSearchTypeEnum.CosineSimilarity;
        
        private static int _BatchSize = 2;
        private static int _MaxParallelTasks = 4;
        private static int _MaxRetries = 3;
        private static int _MaxFailures = 3;

        private static LiteGraphSdk _LiteGraphSdk;
        private static ViewSemanticCellSdk _ViewSemanticCellSdk;
        private static ViewEmbeddingsServerSdk _ViewEmbeddingsSdk;
        
        private static bool _RunForever = true;
        
        #endregion

        #region Constructors-Factories
        
        #endregion
        
        #region Entry-Point

        /// <summary>
        /// Main
        /// </summary>
        public static async Task Main(string[] args)
        {
            _ViewEndpoint = Inputty.GetString("View Endpoint URL:", _ViewEndpoint, false);
            _AccessKey = Inputty.GetString("Access key:", _AccessKey, false);
            
            _LiteGraphSdk = new LiteGraphSdk(_LitegraphEndpoint, _LitegraphBearerToken);
            _ViewSemanticCellSdk = new ViewSemanticCellSdk(_Tenant, _AccessKey, _ViewEndpoint);
            _ViewEmbeddingsSdk = new ViewEmbeddingsServerSdk(_Tenant, _ViewEndpoint, _AccessKey);
            
            if (_EnableLogging)
            {
                _ViewSemanticCellSdk.Logger += (sev, msg) =>
                {
                    if (!string.IsNullOrEmpty(msg))
                        Console.WriteLine($"{sev} {msg}");
                };
                
                _ViewEmbeddingsSdk.Logger += (sev, msg) =>
                {
                    if (!string.IsNullOrEmpty(msg))
                        Console.WriteLine($"{sev} {msg}");
                };
                
                _LiteGraphSdk.Logger += (sev, msg) =>
                {
                    if (!string.IsNullOrEmpty(msg))
                        Console.WriteLine($"{sev} {msg}");
                };
            }
            
            while (_RunForever)
            {
                string userInput = Inputty.GetString("Command [?/help]:", null, false);

                switch (userInput)
                {
                    case "?":
                        Menu();
                        break;
                    case "q":
                        _RunForever = false;
                        break;
                    case "cls":
                        Console.Clear();
                        break;

                    case "conn":
                        ValidateConnectivity();
                        break;
                    
                    case "ingest":
                        await IngestFile();
                        break;
                    
                    case "vsearch":
                        await VectorSearch();
                        break;
                    
                    case "search-type":
                        SetSearchType();
                        break;
                }

                Console.WriteLine("");
            }
        }

        #endregion

        #region Public-Methods
        
        #endregion

        #region Private-Methods

        /// <summary>
        /// Ingests a file from disk, runs it through semantic processing, creates nodes/edges, and generates embeddings.
        /// </summary>
        private static async Task IngestFile()
        {
            string filePath = Inputty.GetString("Enter the file path:", null, false);
            if (!File.Exists(filePath))
            {
                Console.WriteLine("File not found!");
                return;
            }
            
            byte[] fileBytes = File.ReadAllBytes(filePath);
            Console.WriteLine($"Loaded file {filePath}, size {fileBytes.Length} bytes");
            
            var typeInfo = await DetectFileType(fileBytes);
            if (typeInfo == null)
            {
                Console.WriteLine("Failed to detect file type.");
                return;
            }
            Console.WriteLine($"Type Detection: {typeInfo.MimeType}, {typeInfo.Extension}, {typeInfo.Type}");
            
            var mdRule = new MetadataRule
            {
                ProcessingEndpoint = "http://viewdemo:8000/",
                ProcessingAccessKey = "00000000-0000-0000-0000-000000000000",
                MinChunkContentLength = 1,
                MaxChunkContentLength = 512,
                ShiftSize = 512
            };

            var docTypeEnum = typeInfo.Type;
            
            View.Sdk.Semantic.SemanticCellResponse scr = 
                await _ViewSemanticCellSdk.Process(docTypeEnum, mdRule, fileBytes);
            
            var docNode = new LiteGraph.Sdk.Node
            {
                GUID = Guid.NewGuid(),
                TenantGUID = _Tenant,
                GraphGUID = _Graph,
                Name = Path.GetFileName(filePath),
                Labels = new List<string> { "document" },
                Tags = new NameValueCollection
                {
                    { "DocumentType", typeInfo.Type.ToString() },
                    { "Extension",    typeInfo.Extension },
                    { "NodeType",     "Document" },
                    { "MimeType",     typeInfo.MimeType },
                    { "FileName",     Path.GetFileName(filePath) },
                    { "FilePath",     filePath},
                    { "ContentLength", fileBytes.Length.ToString() }
                },
                Data = scr.SemanticCells 
            };

            var createdDocNode = await _LiteGraphSdk.CreateNode(_Tenant, _Graph, docNode);
            if (createdDocNode == null)
            {
                Console.WriteLine("Failed to create document node in LiteGraph");
                return;
            }
            Console.WriteLine($"Document node created in LiteGraph with GUID {createdDocNode.GUID}");
            
            if (scr.SemanticCells == null || !scr.SemanticCells.Any())
            {
                Console.WriteLine("No semantic cells found in the response.");
                return;
            }

            var cellNodes = scr.SemanticCells.Select((cell, index) => new LiteGraph.Sdk.Node
            {
                GUID = Guid.NewGuid(),
                TenantGUID = _Tenant,
                GraphGUID = _Graph,
                Name = $"Cell {cell.GUID}",
                Labels = new List<string> { "semantic-cell" },
                Tags = new NameValueCollection
                {
                    { "CellType", cell.CellType.ToString() },
                    { "NodeType", "SemanticCell" },
                    { "MD5Hash", cell.MD5Hash },
                    { "SHA1Hash", cell.SHA1Hash },
                    { "SHA256Hash", cell.SHA256Hash },
                    { "Position", cell.Position.ToString() },
                    { "Length", cell.Length.ToString() }
                }
            }).ToList();
            
            var createdCellNodes = await _LiteGraphSdk.CreateNodes(_Tenant, _Graph, cellNodes);
            if (createdCellNodes == null || !createdCellNodes.Any())
            {
                Console.WriteLine("Failed to create any cell nodes in LiteGraph");
                return;
            }
            Console.WriteLine($"Created {createdCellNodes.Count} cell nodes in LiteGraph.");
            
            var docToCellEdges = createdCellNodes.Zip(scr.SemanticCells, (cellNode, cell) => new Edge
            {
                GUID = Guid.NewGuid(),
                TenantGUID = _Tenant,
                GraphGUID = _Graph,
                From = createdDocNode.GUID,
                To = cellNode.GUID,
                Name = $"Doc->Cell {cell.GUID}",
                Labels = new List<string> { "edge", "document-cell" },
                Tags = new NameValueCollection
                {
                    { "Relationship", "ContainsCell" },
                    { "EdgeType", "DocumentToCell" }
                }
            }).ToList();

            var createdDocToCellEdges = await _LiteGraphSdk.CreateEdges(_Tenant, _Graph, docToCellEdges);
            if (createdDocToCellEdges == null || createdDocToCellEdges.Count == 0)
                Console.WriteLine("Failed to create doc->cell edges in bulk");
            else
                Console.WriteLine($"Created {createdDocToCellEdges.Count} doc->cell edges in bulk");
            
            var allChunkNodes = new List<LiteGraph.Sdk.Node>();
            var cellToChunkIndices = new Dictionary<int, List<int>>();

            for (int i = 0; i < scr.SemanticCells.Count; i++)
            {
                var cell = scr.SemanticCells[i];
                if (cell.Chunks == null) continue;

                cellToChunkIndices[i] = new List<int>();

                foreach (var chunk in cell.Chunks)
                {
                    var chunkTags = new NameValueCollection();

                    if (!string.IsNullOrEmpty(chunk.MD5Hash)) chunkTags.Add("MD5Hash", chunk.MD5Hash);
                    if (!string.IsNullOrEmpty(chunk.SHA1Hash)) chunkTags.Add("SHA1Hash", chunk.SHA1Hash);
                    if (!string.IsNullOrEmpty(chunk.SHA256Hash)) chunkTags.Add("SHA256Hash", chunk.SHA256Hash);

                    chunkTags.Add("Position", chunk.Position.ToString());
                    chunkTags.Add("Start", chunk.Start.ToString());
                    chunkTags.Add("End", chunk.End.ToString());
                    chunkTags.Add("Length", chunk.Length.ToString());
                    if (!string.IsNullOrEmpty(chunk.Content)) chunkTags.Add("Content", chunk.Content);
                    chunkTags.Add("NodeType", "SemanticChunk");

                    var chunkNode = new LiteGraph.Sdk.Node
                    {
                        GUID = Guid.NewGuid(),
                        TenantGUID = _Tenant,
                        GraphGUID = _Graph,
                        Name = $"Chunk {chunk.GUID}",
                        Labels = new List<string> { "semantic-chunk" },
                        Data = chunk,
                        Tags = chunkTags
                    };
                    cellToChunkIndices[i].Add(allChunkNodes.Count);
                    allChunkNodes.Add(chunkNode);
                }
            }

            var createdChunkNodes = await _LiteGraphSdk.CreateNodes(_Tenant, _Graph, allChunkNodes);
            if (createdChunkNodes == null || !createdChunkNodes.Any())
            {
                Console.WriteLine("Failed to create any chunk nodes in LiteGraph");
            }
            else
            {
                Console.WriteLine($"Created {createdChunkNodes.Count} chunk nodes in LiteGraph.");
                
                var cellToChunkEdges = new List<Edge>();

                for (int i = 0; i < scr.SemanticCells.Count; i++)
                {
                    var cell = scr.SemanticCells[i];
                    if (cell.Chunks == null || !cell.Chunks.Any()) continue;
                    
                    var cellNode = createdCellNodes[i];

                    for (int chunkIdx = 0; chunkIdx < cell.Chunks.Count; chunkIdx++)
                    {
                        var chunk = cell.Chunks[chunkIdx];
                        var indexInAllChunkNodes = cellToChunkIndices[i][chunkIdx];
                        var createdChunkNode = createdChunkNodes[indexInAllChunkNodes];

                        var edge = new Edge
                        {
                            GUID = Guid.NewGuid(),
                            TenantGUID = _Tenant,
                            GraphGUID = _Graph,
                            From = cellNode.GUID,
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
                            },
                            Data = chunk.Content
                        };

                        cellToChunkEdges.Add(edge);
                    }
                }

                if (cellToChunkEdges.Count > 0)
                {
                    var createdCellToChunkEdges = 
                        await _LiteGraphSdk.CreateEdges(_Tenant, _Graph, cellToChunkEdges);

                    if (createdCellToChunkEdges == null || createdCellToChunkEdges.Count == 0)
                        Console.WriteLine("Failed to create cell->chunk edges in bulk");
                    else
                        Console.WriteLine($"Successfully created {createdCellToChunkEdges.Count} cell->chunk edges in bulk");
                }
                else
                {
                    Console.WriteLine("No cell->chunk edges to create (no chunks).");
                }
            }
            
            var chunkContents = new List<string>();
            var cellToChunkMappings = new List<(int cellIndex, int chunkIndex)>();

            for (int i = 0; i < scr.SemanticCells.Count; i++)
            {
                var cell = scr.SemanticCells[i];
                if (cell.Chunks == null || !cell.Chunks.Any()) continue;

                for (int j = 0; j < cell.Chunks.Count; j++)
                {
                    var chunk = cell.Chunks[j];
                    chunkContents.Add(chunk.Content);
                    cellToChunkMappings.Add((i, j));
                }
            }
            
            EmbeddingsGeneratorEnum generator = GetGeneratorType();
            string url = GetBaseUrl(generator);
            string apiKey = GetApiKey();
            string model = GetModel(generator);

            var req = new EmbeddingsRequest
            {
                EmbeddingsRule = new EmbeddingsRule
                {
                    EmbeddingsGenerator = generator,
                    EmbeddingsGeneratorUrl = url,
                    EmbeddingsGeneratorApiKey = apiKey,
                    BatchSize = _BatchSize,
                    MaxGeneratorTasks = _MaxParallelTasks,
                    MaxRetries = _MaxRetries,
                    MaxFailures = _MaxFailures
                },
                Model = model,
                Contents = chunkContents
            };

            var embeddingsResult = await _ViewEmbeddingsSdk.GenerateEmbeddings(req);
            if (!embeddingsResult.Success)
            {
                Console.WriteLine($"Embeddings generation failed: {embeddingsResult.StatusCode}");
                if (embeddingsResult.Error != null)
                {
                    Console.WriteLine($"Error: {embeddingsResult.Error.Message}");
                }
                return;
            }

            if (embeddingsResult.ContentEmbeddings != null && embeddingsResult.ContentEmbeddings.Any())
            {
                await Task.WhenAll(embeddingsResult.ContentEmbeddings
                    .Zip(cellToChunkMappings, (ce, mapping) => new { ContentEmbedding = ce, Mapping = mapping })
                    .Select(async item =>
                    {
                        var (cellIndex, chunkIndex) = item.Mapping;
                        var chunk = scr.SemanticCells[cellIndex].Chunks[chunkIndex];
                        chunk.Embeddings = item.ContentEmbedding.Embeddings;

                        var indexInAllChunkNodes = cellToChunkIndices[cellIndex][chunkIndex];
                        var chunkNode = createdChunkNodes[indexInAllChunkNodes];

                        chunkNode.Data = null;
                        chunkNode.Vectors = new List<VectorMetadata>
                        {
                            new VectorMetadata
                            {
                                TenantGUID = _Tenant,
                                GraphGUID = _Graph,
                                NodeGUID = chunkNode.GUID,
                                Model = "all-MiniLM-L6-v2",
                                Dimensionality = item.ContentEmbedding.Embeddings?.Count ?? 0,
                                Vectors = item.ContentEmbedding.Embeddings,
                                Content = chunk.Content
                            }
                        };

                        return await _LiteGraphSdk.UpdateNode(_Tenant, _Graph, chunkNode);
                    }));
                
                Console.WriteLine("Updated all chunk nodes with embeddings");
            }
            
            Console.WriteLine($"Processed embeddings for {embeddingsResult?.ContentEmbeddings?.Count} chunks");
        }

        private static async Task<TypeResult> DetectFileType(byte[] fileBytes)
        {
            var td = new ViewTypeDetectorSdk(_Tenant, _AccessKey, _ViewEndpoint);
            return await td.Process(fileBytes);
        }

        private static async Task ValidateConnectivity()
        {
            Console.WriteLine("");
            Console.Write("State: ");

            if (await _ViewSemanticCellSdk.ValidateConnectivity())
                Console.WriteLine("View Semantic Cell connected");
            else
                Console.WriteLine("Not connected");
            
            if (await _ViewEmbeddingsSdk.ValidateConnectivity())
                Console.WriteLine("View Embeddings connected");
            else
                Console.WriteLine("View Embeddings not connected");
            
            if (await _LiteGraphSdk.ValidateConnectivity())
                Console.WriteLine("LiteGraph connected");
            else
                Console.WriteLine("LiteGraph not connected");

            Console.WriteLine("");
        }
        
        
        private static EmbeddingsGeneratorEnum GetGeneratorType()
        {
            return (EmbeddingsGeneratorEnum)(Enum.Parse(
                typeof(EmbeddingsGeneratorEnum),
                Inputty.GetString("Generator type [LCProxy/OpenAI/Ollama/VoyageAI]:", "LCProxy", false)));

        }

        private static string GetBaseUrl(EmbeddingsGeneratorEnum generator)
        {
            string url = "";
            if (generator == EmbeddingsGeneratorEnum.LCProxy) url = "http://nginx-lcproxy:8000/";
            else if (generator == EmbeddingsGeneratorEnum.OpenAI) url = "https://api.openai.com/";
            else if (generator == EmbeddingsGeneratorEnum.VoyageAI) url = "https://api.voyageai.com/";
            else if (generator == EmbeddingsGeneratorEnum.Ollama) url = "http://localhost:11434/";
            return Inputty.GetString("URL:", url, false);
        }

        private static string  GetApiKey()
        {
            return Inputty.GetString("API key:", null, true);
        }

        private static string GetModel(EmbeddingsGeneratorEnum generator)
        {
            string model = "";
            if (generator == EmbeddingsGeneratorEnum.LCProxy) model = "all-MiniLM-L6-v2";
            else if (generator == EmbeddingsGeneratorEnum.OpenAI) model = "text-embedding-ada-002";
            else if (generator == EmbeddingsGeneratorEnum.VoyageAI) model = "voyage-3-large";
            else if (generator == EmbeddingsGeneratorEnum.Ollama) model = "all-minilm";
            return Inputty.GetString("Model:", model, false);
        }
        
        /// <summary>
        /// Detects if a string looks like [x, y, z] list notation.
        /// Returns a list of trimmed strings if so.
        /// </summary>
        private static bool IsListNotation(string candidate, out List<string> elements)
        {
            elements = new List<string>();

            if (candidate.StartsWith("[") && candidate.EndsWith("]"))
            {
                string inner = candidate.Substring(1, candidate.Length - 2);
                string[] parts = inner.Split(',');
                foreach (var part in parts)
                {
                    string p = part.Trim();
                    if (!string.IsNullOrEmpty(p)) elements.Add(p);
                }
                return true;
            }
            return false;
        }
        
        private static void Menu()
        {
            Console.WriteLine("");
            Console.WriteLine("Available commands:");
            Console.WriteLine("  ?               help, this menu");
            Console.WriteLine("  q               quit");
            Console.WriteLine("  cls             clear the screen");
            Console.WriteLine("  conn            validate connectivity");
            Console.WriteLine("  ingest          ingest a file");
            Console.WriteLine("  vsearch         enter prompt for vector search");
            Console.WriteLine("  search-type     set the vector search type (currently " + _SearchType + ")");
            Console.WriteLine("");
        }
        
        private static void SetSearchType()
        {
            Console.WriteLine("\nSelect one of the following search types:");
            foreach (var value in Enum.GetNames(typeof(VectorSearchTypeEnum)))
            {
                Console.WriteLine("  " + value);
            }
        
            string choice = Inputty.GetString($"Which search type do you want to use? (current: {_SearchType})",
                _SearchType.ToString(), false);
        
            if (Enum.TryParse(choice, true, out VectorSearchTypeEnum parsed))
            {
                _SearchType = parsed;
                Console.WriteLine($"Search type is now set to {_SearchType}.\n");
            }
            else
            {
                Console.WriteLine("Invalid search type. No changes made.\n");
            }
        }
        
        /// <summary>
        /// Executes a vector search with the currently selected search type.
        /// </summary>
        private static async Task? VectorSearch()
        {
            string searchQuery = Inputty.GetString("Enter a search query:", null, false);
            
            EmbeddingsGeneratorEnum generator = GetGeneratorType();
            string url = GetBaseUrl(generator);
            string apiKey = GetApiKey();
            string model = GetModel(generator);

            Console.WriteLine($"\nProcessing search query: \"{searchQuery}\"");
            
            var searchEmbeddingRequest = new EmbeddingsRequest
            {
                EmbeddingsRule = new EmbeddingsRule
                {
                    EmbeddingsGenerator = generator,
                    EmbeddingsGeneratorUrl = url,
                    EmbeddingsGeneratorApiKey = apiKey,
                    BatchSize = _BatchSize,
                    MaxGeneratorTasks = _MaxParallelTasks,
                    MaxRetries = _MaxRetries,
                    MaxFailures = _MaxFailures
                },
                Model = model,
                Contents = new List<string> { searchQuery }
            };

            var embeddingResult = await _ViewEmbeddingsSdk.GenerateEmbeddings(searchEmbeddingRequest);

            if (!embeddingResult.Success 
                || embeddingResult.ContentEmbeddings == null
                || embeddingResult.ContentEmbeddings.Count == 0
                || embeddingResult.ContentEmbeddings[0].Embeddings == null
                || embeddingResult.ContentEmbeddings[0].Embeddings.Count == 0)
            {
                Console.WriteLine("Failed to generate a non-empty embedding for the search query");
                return;
            }

            Console.WriteLine("Generated embeddings for search query");

            var searchRequest = new VectorSearchRequest
            {
                TenantGUID = _Tenant,
                GraphGUID = _Graph,
                // ToDo: Should this be Node or Graph?
                Domain = VectorSearchDomainEnum.Node,
                SearchType = _SearchType,
                Embeddings = embeddingResult.ContentEmbeddings[0].Embeddings
            };

            var results = await _LiteGraphSdk.SearchVectors(_Tenant, _Graph, searchRequest);
            if (results == null || !results.Any())
            {
                Console.WriteLine("No matches found");
                return;
            }
            
            Console.WriteLine($"Found {results.Count} matches.");

            IEnumerable<VectorSearchResult> sortedResults = _SearchType switch
            {
                VectorSearchTypeEnum.CosineSimilarity => results.OrderByDescending(r => r.Score),
                VectorSearchTypeEnum.DotProduct => results.OrderByDescending(r => r.Score),
                VectorSearchTypeEnum.EuclidianDistance => results.OrderBy(r => r.Score),
                VectorSearchTypeEnum.EuclidianSimilarity => results.OrderByDescending(r => r.Score),
                _ => results
            };
            
            var table = new ConsoleTable("Node GUID", $"{_SearchType} Search Score" , "Contents");

            foreach (var result in sortedResults)
            {
                string rawContent = result.Node.Tags["Content"] ?? "[No Content]";
                
                string content;
                if (IsListNotation(rawContent, out var elements))
                {
                    var joined = string.Join(" ", elements);
                    content = joined.Length <= 40 ? joined : joined.Substring(0, 40);
                }
                else
                {
                    content = rawContent.Length <= 40 ? rawContent : rawContent.Substring(0, 40);
                }

                table.AddRow(result.Node.GUID, 
                    result.Score.HasValue ? result.Score.Value.ToString("F3") : "N/A", 
                    content);
            }

            Console.WriteLine(table);
        }

        #endregion
                
    }
}
