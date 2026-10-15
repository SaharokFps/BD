

CREATE TABLE IF NOT EXISTS Nodes (
    NodeId      TEXT PRIMARY KEY,      
    DomainName  TEXT UNIQUE,           
    IpAddress   TEXT,
    Platform    TEXT,                  
    OsVersion   TEXT,
    IsMaster    INTEGER DEFAULT 0,
    LastSeen    TEXT                   
);

CREATE TABLE IF NOT EXISTS Components (
    Id           INTEGER PRIMARY KEY AUTOINCREMENT,
    NodeId       TEXT NOT NULL,
    Category     TEXT NOT NULL,        
    Name         TEXT,
    Vendor       TEXT,
    SerialNumber TEXT,
    Specs        TEXT,                 
    Source       TEXT,                 
    CapturedAt   TEXT,
    FOREIGN KEY(NodeId) REFERENCES Nodes(NodeId) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS OsInfo (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    NodeId        TEXT NOT NULL,
    Caption       TEXT,                
    Version       TEXT,
    BuildNumber   TEXT,
    Kernel        TEXT,                
    Architecture  TEXT,                
    InstallDate   TEXT,
    CapturedAt    TEXT,
    FOREIGN KEY(NodeId) REFERENCES Nodes(NodeId) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ReplicationQueue (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Payload     BLOB,
    TargetNode  TEXT,
    Status      TEXT,                  
    CreatedAt   TEXT
);

CREATE TABLE IF NOT EXISTS StaticPeers (
    DomainName TEXT PRIMARY KEY,
    IpAddress  TEXT,
    Platform   TEXT
);


CREATE INDEX IF NOT EXISTS IX_Components_NodeId   ON Components(NodeId);
CREATE INDEX IF NOT EXISTS IX_Components_Category ON Components(Category);
CREATE INDEX IF NOT EXISTS IX_OsInfo_NodeId       ON OsInfo(NodeId);
CREATE INDEX IF NOT EXISTS IX_Nodes_LastSeen      ON Nodes(LastSeen);