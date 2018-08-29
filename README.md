# High Throughput Uploads of Individual Block Blobs using .NET

This application serves as an example of uploading the content of a block blob by issuing `PutBlock` commands in parallel
across 1 to n nodes - making full use of allocated bandwidth for your account.

To make full use of the bandwidth allocated to your storage account, you will most likely need to run multiple instances
of this application in parallel.
1) Given the maximum upload bandwidth of a single node from which you will run the test, determine how many nodes are
required to reach the data ingress limits of your account.
2) Run one instance of this application on each node - providing the following arguments to each instance:
	1) The total number of blocks to be uploaded in the test.
	2) The 0-based ID for the instance.  Must be unique for each instance and be between (inclusive) 0 and TotalInstances-1.
	3) The total number of instances running in the test.

```
Note: The total number of blocks will be distributed across each instance.
Note: Instance 0 will remain running until all blocks are successfully uploaded.
```

## Prerequisites

**NOTE** For best performance, this application should be run atop .NET Core 2.1 or later.

* Install .NET core 2.1 for [Linux](https://www.microsoft.com/net/download/linux) or [Windows](https://www.microsoft.com/net/download/windows)

If you don't have an Azure subscription, create a [free account](https://azure.microsoft.com/free/?WT.mc_id=A261C142F) before you begin.

## Create a storage account using the Azure portal

First, create a new general-purpose storage account to use for this quickstart.

1. Go to the [Azure portal](https://portal.azure.com) and log in using your Azure account. 
2. On the Hub menu, select **New** > **Storage** > **Storage account - blob, file, table, queue**. 
3. Enter a unique name for your storage account. Keep these rules in mind for naming your storage account:
    - The name must be between 3 and 24 characters in length.
    - The name may contain numbers and lowercase letters only.
4. Make sure that the following default values are set: 
    - **Deployment model** is set to **Resource manager**.
    - **Account kind** is set to **General purpose**.
    - **Performance** is set to **Standard**.
    - **Replication** is set to **Locally Redundant storage (LRS)**.
5. Select your subscription. 
6. For **Resource group**, create a new one and give it a unique name. 
7. Select the **Location** to use for your storage account.
8. Check **Pin to dashboard** and click **Create** to create your storage account. 

After your storage account is created, it is pinned to the dashboard. Click on it to open it. Under **Settings**, click **Access keys**. Select the primary key and copy the associated **Connection string** to the clipboard, then paste it into a text editor for later use.

## Put the connection string in an environment variable

This solution requires a connection string be stored in an environment variable securely on the machine running the sample. Follow one of the examples below depending on your Operating System to create the environment variable. If using windows close out of your open IDE or shell and restart it to be able to read the environment variable.

### Linux

```bash
export storageconnectionstring="<yourconnectionstring>"
```
### Windows

```cmd
setx storageconnectionstring "<yourconnectionstring>"
```

At this point, you can run this application. It creates its own file to upload and download, and then cleans up after itself by deleting everything at the end.

## Run the application

Navigate to your application directory and run the application with the `dotnet run` command.
### Arguments
- `arg0: TotalBlocks` (Default: 50000):
Specifies the total number of blocks (across all instances of this application) to be uploaded.
- `arg1: CurrentInstance` (Default: 0)
The index of this instance of the application, starting at 0 (0 <= index < TotalInstances).
- `arg2: TotalInstances` (Default: 1)
The total number of instances dedicated to uploading this blob.

### Examples of Single-Instance Tests

```
dotnet run
```
The application will:
- Upload 50000 100MB blocks with IDs ranging from Base64(`0x00000000`) to Base64(`0x0000C34F`)
- Issue a GetBlockList to verify all uploaded blocks are in the uncommitted block list.
- Issue a PutBlockList for the uploaded blocks.
- Issue a DeleteBlob.
---
```
dotnet run 1024
```
The application will:
- Upload 1024 100MB blocks with IDs ranging from Base64(`0x00000000`) to Base64(`0x000003FF`)
- Issue a GetBlockList to verify all 1024 expected blocks are in the uncommitted block list.
- Issue a PutBlockList for the uploaded blocks.
- Issue a DeleteBlob.
### Example of Multiple-Instance Test
```
dotnet run 1024 0 2
```
The application will:
- Upload 512 100MB blocks with IDs ranging from Base64(`0x00000000`) to Base64(`0x000001FF`)
- Wait until all 1024 expected blocks in the uncommitted block list (by periodically issuing GetBlockList).
- Issue a PutBlockList for the uploaded blocks.
- Issue a DeleteBlob.

```
dotnet run 1024 1 2
```
The application will:
- Upload 512 100MB blocks with IDs ranging from Base64(`0x00000200`) to Base64(`0x000003FF`)

## More information

The [Azure storage documentation](https://docs.microsoft.com/azure/storage/) includes a rich set of tutorials and conceptual articles, which serve as a good complement to the samples.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
