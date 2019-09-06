//------------------------------------------------------------------------------
//MIT License

//Copyright(c) 2018 Microsoft Corporation. All rights reserved.

//Permission is hereby granted, free of charge, to any person obtaining a copy
//of this software and associated documentation files (the "Software"), to deal
//in the Software without restriction, including without limitation the rights
//to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//copies of the Software, and to permit persons to whom the Software is
//furnished to do so, subject to the following conditions:

//The above copyright notice and this permission notice shall be included in all
//copies or substantial portions of the Software.

//THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//SOFTWARE.
//------------------------------------------------------------------------------

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


namespace Sample_HighThroughputBlobUpload
{
    /// <summary>
    /// This application serves as an example of uploading the content of a block blob by issuing PutBlock commands in parallel across 1 to n nodes -
    /// making full use of allocated bandwidth for your account.
    /// </summary>
    /// <remarks>
    /// See README.md for details on how to run this application.
    /// 
    /// NOTE: For performance reasons, this code is best run on .NET Core v2.1 or later.
    /// 
    /// Documentation References: 
    /// - What is a Storage Account - https://docs.microsoft.com/azure/storage/common/storage-create-storage-account
    /// - Getting Started with Blobs - https://docs.microsoft.com/azure/storage/blobs/storage-dotnet-how-to-use-blobs
    /// - Blob Service Concepts - https://docs.microsoft.com/rest/api/storageservices/Blob-Service-Concepts
    /// - Blob Service REST API - https://docs.microsoft.com/rest/api/storageservices/Blob-Service-REST-API
    /// - Blob Service C# API - https://docs.microsoft.com/dotnet/api/overview/azure/storage?view=azure-dotnet
    /// - Scalability and performance targets - https://docs.microsoft.com/azure/storage/common/storage-scalability-targets
    /// - Azure Storage Performance and Scalability checklist https://docs.microsoft.com/azure/storage/common/storage-performance-checklist
    /// - Storage Emulator - https://docs.microsoft.com/azure/storage/common/storage-use-emulator
    /// - Asynchronous Programming with Async and Await  - http://msdn.microsoft.com/library/hh191443.aspx
    /// </remarks>
    class Program
    {
        static void Main(string[] args)
        {
            string containerName = $"highthroughputblobsample";
            string blobName = $"sampleBlob";

            if (!ParseArguments(args, out uint totalBlocks, out uint instanceId, out uint totalInstances))
            {
                return;
            }

            // Determine the number of blocks this instance is responsible for.
            uint blocksPerInstance = totalBlocks / totalInstances;
            uint nBlocks = blocksPerInstance;
            // Have the last instance pick up the remainder.
            if (instanceId + 1 == totalInstances)
            {
                nBlocks += totalBlocks % totalInstances;
            }

            uint startingBlockId = instanceId * blocksPerInstance;

            try
            {
                // Load the connection string for use with the application. The storage connection string is stored
                // in an environment variable on the machine running the application called storageconnectionstring.
                // If the environment variable is created after the application is launched in a console or with Visual
                // Studio, the shell needs to be closed and reloaded to take the environment variable into account.
                string storageConnectionString = Environment.GetEnvironmentVariable("storageconnectionstring");
                if (string.IsNullOrEmpty(storageConnectionString))
                {
                    throw new Exception("Unable to connect to storage account. The environment variable 'storageconnectionstring' is not defined. See README.md for details.");
                }
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();

                // Create the container if it doesn't yet exist.
                CloudBlobContainer container = blobClient.GetContainerReference(containerName);
                container.CreateIfNotExistsAsync().Wait();

                // Execute the operation.
                Console.WriteLine($"Begin uploading blob '{blobName}' to container '{containerName}'.");
                Task<TimeSpan> uploadBlobTask = UploadBlocksAsync(blobClient, container, blobName, nBlocks, startingBlockId);
                uploadBlobTask.Wait();


                TimeSpan elapsedTime = uploadBlobTask.Result;
                Console.WriteLine($"Upload complete. {nBlocks} blocks ({nBlocks * 100} MiB) uploaded in {elapsedTime.TotalSeconds} seconds.");

                // If this is the 0th instance, it is responsible for committing the block list.
                if (instanceId == 0)
                {
                    FinalizeBlob(container, blobName, totalBlocks).Wait();
                }

                Console.WriteLine("Test Complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Test failed.  Details: {ex.Message}");
            }

            Console.ReadLine();

        }

        /// <summary>
        /// Responsible for uploading the specified number of 100MiB blocks to the named blob.
        /// </summary>
        /// <param name="blobClient">The client to use when interracting with the Blob service.</param>
        /// <param name="container">The targeted blob container.</param>
        /// <param name="blobName">The name of the blob being uploaded.</param>
        /// <param name="nBlocks">The number of blocks to upload.</param>
        /// <param name="startingBlockId">The ID to use for the first block uploaded by this method.</param>
        /// <returns></returns>
        public static async Task<TimeSpan> UploadBlocksAsync
            (CloudBlobClient blobClient,
             CloudBlobContainer container,
             string blobName,
             uint nBlocks,
             uint startingBlockId)
        {
            // Cap the number of parallel PutBlob requests from this process.
            const int MAX_CONCURRENCY = 128;

            // Create a 100 MiB buffer (size of largest acceptable block) filled with random data.
            byte[] buffer = new byte[100 * 1024 * 1024];
            Random rand = new Random();
            rand.NextBytes(buffer);

            try
            {
                CloudBlockBlob blockBlob = container.GetBlockBlobReference(blobName);

                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();

                List<Task> tasks = new List<Task>();
                SemaphoreSlim semaphore = new SemaphoreSlim(MAX_CONCURRENCY, MAX_CONCURRENCY);

                for (uint blockId = startingBlockId; blockId < startingBlockId + nBlocks; ++blockId)
                {
                    MemoryStream ms = new MemoryStream(buffer);
                    // Generate the Block ID from the Base64 representation of the 32-bit index.
                    string blockIdStr = Convert.ToBase64String(BitConverter.GetBytes(blockId));

                    await semaphore.WaitAsync();

                    tasks.Add(blockBlob.PutBlockAsync(blockIdStr, ms, null).ContinueWith((t) => { semaphore.Release(); }));
                }

                Console.WriteLine("All PutBlock requests issued.  Waiting for the remainder to complete.");
                Task completionTask = Task.WhenAll(tasks);
                await completionTask;

                // If any failures occurred, communicate them and propagate an aggregate exception to indicate a failed test.
                if (completionTask.IsFaulted)
                {
                    Console.WriteLine("An error occurred while executing one or more of the PutBlock operations. Details:");
                    foreach (Exception ex in completionTask.Exception.InnerExceptions)
                    {
                        Console.WriteLine($"\t{ex.Message}");
                    }
                    throw completionTask.Exception;
                }

                stopwatch.Stop();

                return stopwatch.Elapsed;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while executing UploadBlocksAsync.  Details: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Parses the command line arguments provided to the application, provides default values as necessary, and checks their validity.
        /// </summary>
        /// <param name="args">The </param>
        /// <param name="totalBlocks">The total number of blocks uploaded across the test.</param>
        /// <param name="currentInstance">The current </param>
        /// <param name="totalInstances"></param>
        /// <returns>Returns true if the provided arguments were valid; otherwise, returns false.</returns>
        static bool ParseArguments(string[] args, out uint totalBlocks, out uint currentInstance, out uint totalInstances)
        {
            const uint MAX_BLOCKS = 50000; // Maximum number of blocks that can be committed.
            bool isValid = true;
            totalBlocks = MAX_BLOCKS;
            currentInstance = 0;
            totalInstances = 1;
            try
            {
                if (args.Length > 0)
                {
                    totalBlocks = System.Convert.ToUInt32(args[0]);
                }
                if (args.Length > 1)
                {
                    if (args.Length == 3)
                    {
                        currentInstance = System.Convert.ToUInt32(args[1]);
                        totalInstances = System.Convert.ToUInt32(args[2]);
                    }
                    else
                    {
                        isValid = false;
                    }
                }
            }
            catch (Exception)
            {
                isValid = false;
            }

            if (!isValid)
            {
                Console.WriteLine("Invalid Arguments Provided.  Expected Arguments: arg0:totalBlocks arg1:currentInstance arg2:totalInstances");
            }
            // Follow-up with a few logical checks.
            else
            {
                if (totalBlocks < totalInstances)
                {
                    Console.WriteLine("totalBlocks (arg0) must be >= totalInstances (arg1).");
                    isValid = false;
                }

                if (totalBlocks > MAX_BLOCKS)
                {
                    Console.WriteLine($"totalBlocks (arg0) must be less than the maximum number of committable blocks ({MAX_BLOCKS}).");
                    isValid = false;
                }
            }

            return isValid;
        }

        /// <summary>
        /// Waits for all expected blocks to finish uploading, commits the blob, then deletes the blob and container used during the test.
        /// </summary>
        /// <remarks>This method will only be called if application is running as ID 0.</remarks>
        /// <param name="container">The blob's container.</param>
        /// <param name="blobName">The name of the blob being uploaded.</param>
        /// <param name="totalBlocks">The total number of blocks to commit to the blob.</param>
        static async Task FinalizeBlob(CloudBlobContainer container, string blobName, uint totalBlocks)
        {
            // Define the order in which to commit blocks.
            List<string> blockIdList = new List<string>();
            for (uint i = 0; i < totalBlocks; ++i)
            {
                blockIdList.Add(Convert.ToBase64String(BitConverter.GetBytes(i)));

            }
            HashSet<string> blockIdSet = new HashSet<string>(blockIdList);

            CloudBlockBlob blockblob = container.GetBlockBlobReference(blobName);

            // Poll the blob's Uncommitted Block List until we determine that all expected blocks have been successfully uploaded.
            Console.WriteLine("Waiting for all expected blocks to appear in the uncommitted block list.");
            IEnumerable<ListBlockItem> currentUncommittedBlockList = new List<ListBlockItem>();
            while (true)
            {
                currentUncommittedBlockList = await blockblob.DownloadBlockListAsync(BlockListingFilter.Uncommitted, AccessCondition.GenerateEmptyCondition(), null, null);
                if (currentUncommittedBlockList.Count() >= blockIdList.Count &&
                    VerifyBlocks(currentUncommittedBlockList, blockIdSet))
                {
                    break;
                }
                Console.WriteLine($"{currentUncommittedBlockList.Count()} / {blockIdList.Count} blocks in the uncommitted block list.");
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            Console.WriteLine("Issuing PutBlockList.");
            await blockblob.PutBlockListAsync(blockIdList);

            Console.WriteLine("Press 'Enter' key to delete the example container and blob.");
            Console.ReadLine();

            Console.WriteLine("Issuing DeleteBlob.");
            await blockblob.DeleteAsync();

            Console.WriteLine("Deleting container.");
            await container.DeleteAsync();
        }

        /// <summary>
        /// Determines whether the uncommitted block list contains all expected blocks.
        /// </summary>
        /// <param name="uncommittedBlockList">A collection of IDs for the blocks in the uncommitted block list.</param>
        /// <param name="blockIdSet">A collection of block IDs that are expected.</param>
        /// <returns>True if all blocks in blockIdSet exist in uncommittedBlockList; otherwise, false.</returns>
        static bool VerifyBlocks(IEnumerable<ListBlockItem> uncommittedBlockList, HashSet<string> blockIdSet)
        {
            uint nMatchingBlocks = 0;
            foreach (ListBlockItem block in uncommittedBlockList)
            {
                if (blockIdSet.Contains(block.Name))
                {
                    ++nMatchingBlocks;
                }
                if (nMatchingBlocks == blockIdSet.Count)
                {
                    return true;
                }   
            }

            return false;
        }
    }

}
