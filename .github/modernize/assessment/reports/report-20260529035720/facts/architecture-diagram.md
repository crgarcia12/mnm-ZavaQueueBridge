# Architecture Diagram

ZavaQueueBridge is a single-process file ingestion worker that watches a shared folder, parses inbound flat files, and publishes JSON messages to RabbitMQ. Its architecture is centered on local file-system coordination and asynchronous broker communication rather than HTTP endpoints or a database.

## Application Architecture

```mermaid
flowchart TD
    subgraph Input["Input Layer"]
        Producer["Upstream File Producer"]
        Drop["Shared Drop Folder"]
        Processed["Processed Folder"]
        Error["Error Folder"]
    end
    subgraph App["Application Layer - .NET Framework 4.8 Console Worker"]
        Watcher["FileSystemWatcher"]
        Poller["Polling Loop"]
        Parser["CSV and Fixed Width Parser"]
        Publisher["Message Publisher"]
        DeadLetter["Dead Letter Publisher"]
    end
    subgraph Messaging["Messaging Layer"]
        Exchange["RabbitMQ Topic Exchange filedrop.events"]
        Dlx["RabbitMQ Fanout Exchange zava.dlx"]
    end
    subgraph Config["Configuration"]
        AppConfig["app.config and Environment Variables"]
    end

    Producer -->|"drops files"| Drop
    Drop -->|"create and change events"| Watcher
    Watcher -->|"queues candidate files"| Poller
    Poller -->|"reads records"| Parser
    Parser -->|"serializes JSON payloads"| Publisher
    Publisher -->|"publishes per record"| Exchange
    Poller -->|"moves successful files"| Processed
    Poller -->|"publishes failures"| DeadLetter
    DeadLetter -->|"dead letter events"| Dlx
    Poller -->|"moves failed files"| Error
    AppConfig -->|"broker, path, and polling settings"| Poller
```

### Technology Stack Summary

| Layer | Technology | Version | Purpose |
| --- | --- | --- | --- |
| Execution | .NET Framework console application | 4.8 | Hosts the long-running file bridge worker |
| File ingestion | System.IO FileSystemWatcher and directory polling | .NET Framework | Detects and batches inbound files from the shared drop folder |
| Messaging | RabbitMQ.Client | 5.2.0 | Publishes parsed records and dead-letter events to RabbitMQ |
| Serialization | JavaScriptSerializer | .NET Framework | Converts parsed records into JSON payloads |
| Configuration | app.config plus environment-variable overrides | N/A | Externalizes broker, path, and poll interval settings |

### Data Storage & External Services

The application does not use a database or cache. Its persistent integration points are the shared file-system directories used for inbound, processed, and error files, plus a RabbitMQ broker that receives per-record topic messages and dead-letter notifications when processing fails.

### Key Architectural Decisions

- Uses a hybrid watcher plus polling model so new files can be discovered both from file-system events and periodic directory scans.
- Publishes one RabbitMQ message per parsed record, allowing downstream consumers to process file contents independently of the original file.
- Treats the file system as the operational state store by moving files into processed or error directories after each run.

## Component Relationships

```mermaid
flowchart LR
    subgraph Host["Execution Host"]
        Main["Program.Main"]
        ConfigLoader["LoadConfig and EnsureDirectories"]
        WatcherComp["ConfigureWatcher"]
    end
    subgraph Business["Business Logic"]
        QueueMgr["QueuePendingFile and PollFiles"]
        Processor["ProcessSingleFile"]
        ParserComp["ParseCsv and ParseFixedWidth"]
    end
    subgraph DataAccess["Data Access"]
        Files["File System Access"]
        Moves["MoveFile"]
    end
    subgraph Infra["Infrastructure"]
        BrokerFactory["CreateFactory"]
        BrokerPublish["BasicPublish"]
        DeadLetterComp["PublishDeadLetter"]
    end

    Main -->|"initializes"| ConfigLoader
    Main -->|"starts watcher"| WatcherComp
    Main -->|"loops"| QueueMgr
    WatcherComp -->|"enqueues paths"| QueueMgr
    QueueMgr -->|"opens files"| Processor
    Processor -->|"reads and parses"| ParserComp
    Processor -->|"reads and moves files"| Files
    Processor -->|"creates broker connection"| BrokerFactory
    BrokerFactory -->|"publishes records"| BrokerPublish
    QueueMgr -->|"on failure"| DeadLetterComp
    DeadLetterComp -->|"publishes error event"| BrokerPublish
    Processor -->|"archives file"| Moves
    DeadLetterComp -->|"routes failed file"| Moves
```

### Component Inventory

| Component | Layer | Type | Responsibility |
| --- | --- | --- | --- |
| Program.Main | Execution Host | Console entry point | Loads configuration, prepares directories, configures the watcher, and drives the polling loop |
| LoadConfig and EnsureDirectories | Execution Host | Startup helpers | Resolve configuration from environment variables and create required folders |
| ConfigureWatcher | Execution Host | File watcher setup | Subscribes to file create, change, and rename events for the drop folder |
| QueuePendingFile and PollFiles | Business Logic | Work coordinator | Deduplicates candidate files and processes each pending batch |
| ProcessSingleFile | Business Logic | File processing service | Waits for file readiness, parses records, publishes messages, and archives the file |
| ParseCsv and ParseFixedWidth | Business Logic | Record parsers | Convert inbound flat-file content into normalized record dictionaries |
| CreateFactory and BasicPublish | Infrastructure | Messaging adapter | Open RabbitMQ connections and publish topic messages |
| PublishDeadLetter | Infrastructure | Error publisher | Emits failure payloads to the dead-letter exchange when file processing fails |
| MoveFile | Data Access | File archive helper | Moves processed or failed files into their destination folders |
