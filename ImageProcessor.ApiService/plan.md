# Distributed Image Processing Pipeline - .NET Learning Project

## 🎯 Project Goal

Build a production-grade distributed image processing system using modern .NET Aspire stack. The purpose is **purely educational** - to learn and practice enterprise-level .NET patterns, microservices architecture, and cloud-native development.

## 📋 What It Does

### User Flow
1. User uploads an image via REST API
2. System stores original in S3 and queues processing job
3. Background worker processes image asynchronously
4. User retrieves processed results via API

### Input
- Single image upload (JPEG, PNG, WebP, etc.)

### Output
```json
{
  "jobId": "abc123",
  "status": "completed",
  "results": {
    "original": "https://cdn.../original.jpg",
    "thumbnails": {
      "small": "https://cdn.../thumb-128.webp",
      "medium": "https://cdn.../thumb-512.webp",
      "large": "https://cdn.../thumb-1024.webp"
    },
    "optimized": {
      "webp": "https://cdn.../optimized.webp",
      "avif": "https://cdn.../optimized.avif"
    },
    "metadata": {
      "width": 4032,
      "height": 3024,
      "format": "jpeg",
      "fileSize": "3.2MB",
      "exif": { "camera": "...", "location": "..." },
      "dominantColors": ["#3B5998", "#8B9DC3", "#DFE3EE"]
    }
  }
}
```

## 🏗️ Architecture

### Services

```
┌─────────────┐      ┌──────────────┐      ┌─────────────┐
│   API       │─────▶│  RabbitMQ    │─────▶│   Worker    │
│  (Upload)   │      │   (Queue)    │      │ (Process)   │
└─────────────┘      └──────────────┘      └─────────────┘
       │                                           │
       ▼                                           ▼
┌─────────────┐                            ┌─────────────┐
│ PostgreSQL  │◀───────────────────────────│     S3      │
│  (Jobs DB)  │                            │  (Storage)  │
└─────────────┘                            └─────────────┘
       │                                           │
       ▼                                           ▼
┌─────────────┐                            ┌─────────────┐
│   Aspire    │                            │ CloudFront  │
│ (Orchestr.) │                            │    (CDN)    │
└─────────────┘                            └─────────────┘
```

### Component Responsibilities

**API Service**
- REST endpoints for upload/status/retrieval
- JWT authentication
- File validation
- Job creation in PostgreSQL
- Message publishing to RabbitMQ
- Response caching with Hybrid Cache

**Worker Service**
- Consume messages from RabbitMQ
- Download from S3
- Image processing (resize, convert, optimize)
- Metadata extraction (EXIF, colors)
- Upload results back to S3
- Update job status in PostgreSQL

**Aspire Host**
- Service orchestration
- Service discovery
- Development dashboard
- Resource management

## 🛠️ Technology Stack

### Core Framework
- **.NET 10.0** - Latest .NET runtime
- **Aspire** - Cloud-native orchestration framework

### Data & Storage
- **PostgreSQL** - Relational database for job tracking
- **Entity Framework Core 13.1.0** - ORM
- **AWS S3** - Object storage for images
- **CloudFront** - CDN for serving processed images

### Messaging & Background Jobs
- **RabbitMQ** - Message queue for async processing
- **Hangfire** - Background job scheduling (cleanup, retries)

### Caching & Resilience
- **Microsoft.Extensions.Caching.Hybrid** - Distributed caching
- **Microsoft.Extensions.Http.Resilience** - Retry policies, circuit breakers

### Authentication & Security
- **JWT Bearer** - API authentication
- **LettuceEncrypt** - Automatic HTTPS certificates

### Observability
- **OpenTelemetry** - Distributed tracing
    - Instrumentation for ASP.NET Core
    - Instrumentation for HTTP
    - Exporter for OpenTelemetry Protocol

### Development Tools
- **Swashbuckle (Swagger)** - API documentation
- **MailPit** - Local email testing
- **JetBrains Rider** - IDE

## 📊 Database Schema

### Jobs Table
```sql
CREATE TABLE jobs (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    status VARCHAR(50) NOT NULL, -- queued, processing, completed, failed
    original_url TEXT NOT NULL,
    original_filename VARCHAR(255) NOT NULL,
    file_size BIGINT NOT NULL,
    mime_type VARCHAR(100) NOT NULL,
    
    -- Results (JSON)
    thumbnails JSONB,
    optimized JSONB,
    metadata JSONB,
    
    -- Timestamps
    created_at TIMESTAMP NOT NULL,
    started_at TIMESTAMP,
    completed_at TIMESTAMP,
    
    -- Error handling
    error_message TEXT,
    retry_count INT DEFAULT 0,
    
    FOREIGN KEY (user_id) REFERENCES users(id)
);

CREATE INDEX idx_jobs_user_id ON jobs(user_id);
CREATE INDEX idx_jobs_status ON jobs(status);
CREATE INDEX idx_jobs_created_at ON jobs(created_at);
```

### Users Table
```sql
CREATE TABLE users (
    id UUID PRIMARY KEY,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    created_at TIMESTAMP NOT NULL,
    last_login TIMESTAMP
);
```

## 🔄 Processing Flow

### 1. Upload Request
```
POST /api/images/upload
Authorization: Bearer <jwt>
Content-Type: multipart/form-data

File: image.jpg
```

**API Logic:**
1. Validate JWT token
2. Validate file (type, size)
3. Upload original to S3: `s3://bucket/originals/{userId}/{jobId}.jpg`
4. Create job record in PostgreSQL (status: queued)
5. Publish message to RabbitMQ queue
6. Return job response immediately

### 2. Background Processing
**Worker consumes from RabbitMQ:**
1. Update job status to "processing"
2. Download original from S3
3. Generate thumbnails (128px, 512px, 1024px)
4. Convert to WebP and AVIF
5. Extract metadata (dimensions, EXIF, dominant colors)
6. Upload all results to S3: `s3://bucket/processed/{userId}/{jobId}/`
7. Update job record with results
8. Update status to "completed"

### 3. Retrieve Results
```
GET /api/images/{jobId}
Authorization: Bearer <jwt>
```

**API Logic:**
1. Check Hybrid Cache first
2. If miss, query PostgreSQL
3. Return job with all URLs
4. Cache result for future requests

### 4. Scheduled Cleanup (Hangfire)
**Daily job:**
- Delete jobs older than 30 days
- Remove associated S3 objects
- Clean up orphaned files

## 🎓 Learning Objectives

### What Each Technology Teaches

**Aspire**
- ✅ Microservices orchestration
- ✅ Service discovery patterns
- ✅ Local-to-production parity
- ✅ Resource dependency management

**Entity Framework Core + PostgreSQL**
- ✅ Code-first migrations
- ✅ Complex queries with LINQ
- ✅ JSON column support
- ✅ Transaction management
- ✅ Optimistic concurrency

**RabbitMQ**
- ✅ Message queue patterns
- ✅ Pub/sub architecture
- ✅ Dead letter queues
- ✅ Message acknowledgment

**Hangfire**
- ✅ Background job scheduling
- ✅ Recurring jobs
- ✅ Job persistence
- ✅ Retry policies

**AWS S3 + CloudFront**
- ✅ Object storage patterns
- ✅ Pre-signed URLs
- ✅ CDN integration
- ✅ Multipart uploads

**Hybrid Caching**
- ✅ L1 (in-memory) + L2 (distributed) caching
- ✅ Cache invalidation strategies
- ✅ Cache-aside pattern

**Http.Resilience**
- ✅ Retry policies with exponential backoff
- ✅ Circuit breaker pattern
- ✅ Timeout policies
- ✅ Bulkhead isolation

**JWT Authentication**
- ✅ Token-based auth
- ✅ Claims-based authorization
- ✅ Token refresh patterns

**OpenTelemetry**
- ✅ Distributed tracing
- ✅ Metrics collection
- ✅ Log correlation
- ✅ Observability best practices

## 🚀 Implementation Phases

### Phase 1: Foundation (Week 1)
- [x] Set up Aspire host project
- [x] Create API and Worker services
- [x] Configure PostgreSQL with EF Core
- [x] Implement basic CRUD for users
- [x] Set up JWT authentication

### Phase 2: File Upload (Week 1-2)
- [x] Implement multipart file upload endpoint
- [x] Integrate AWS S3 SDK (Cloudflare R2 via S3-compatible API)
- [x] Create jobs table and entity
- [x] Implement file validation (extension, MIME type, file size)
- [x] Implement repository pattern (IJobRepository / JobRepository)
- [x] Implement GET /api/images/{jobId} and GET /api/images endpoints
- [x] Add response DTOs to prevent domain model leaking
- [x] Fix ownership check on GET /api/images/{jobId} (any user can query any job ID)
- [x] Add pagination to GET /api/images
- [x] Add basic error handling middleware
- [x] Add structured logging via ILogger<T>

### Phase 3: Message Queue (Week 2)
- [ ] Set up RabbitMQ in Aspire
- [ ] Implement message publisher in API
- [ ] Implement message consumer in Worker
- [ ] Add job status updates
- [ ] Test async flow end-to-end

### Phase 4: Image Processing (Week 2-3)
- [ ] Integrate image processing library (ImageSharp or SkiaSharp)
- [ ] Implement thumbnail generation
- [ ] Implement format conversion (WebP, AVIF)
- [ ] Extract EXIF metadata
- [ ] Calculate dominant colors

### Phase 5: Storage & CDN (Week 3)
- [ ] Upload processed results to S3
- [ ] Configure CloudFront distribution
- [ ] Generate CDN URLs
- [ ] Implement pre-signed URLs for uploads

### Phase 6: Caching (Week 3-4)
- [ ] Set up Hybrid Cache
- [ ] Cache job results
- [ ] Implement cache invalidation
- [ ] Add cache hit/miss metrics

### Phase 7: Resilience (Week 4)
- [ ] Add retry policies for S3 operations
- [ ] Implement circuit breakers
- [ ] Add timeout policies
- [ ] Test failure scenarios

### Phase 8: Background Jobs (Week 4)
- [ ] Set up Hangfire with PostgreSQL storage
- [ ] Implement cleanup job
- [ ] Add failed job retry logic
- [ ] Create admin dashboard

### Phase 9: Observability (Week 5)
- [ ] Configure OpenTelemetry exporters
- [ ] Add custom traces and metrics
- [ ] Set up Aspire dashboard
- [ ] Implement structured logging

### Phase 10: Polish (Week 5-6)
- [ ] Add OpenAPI documentation
- [ ] Write integration tests
- [ ] Add rate limiting
- [ ] Performance optimization
- [ ] Security hardening

## 🧪 API Endpoints

### Authentication
```
POST /api/auth/register
POST /api/auth/login
POST /api/auth/refresh
```

### Images
```
POST   /api/images/upload       - Upload new image
GET    /api/images/{jobId}      - Get job status/results
GET    /api/images              - List user's jobs (paginated)
DELETE /api/images/{jobId}      - Delete job and files
```

### Health
```
GET /health                     - Service health check
GET /health/ready              - Readiness probe
```

## 📈 Potential Extensions

### Phase 2 Features (After Core Complete)
- **AI Integration**
    - Object detection
    - Face detection/blurring
    - NSFW content filtering
    - Auto alt-text generation
    - Image similarity search

- **Advanced Processing**
    - Background removal
    - Smart cropping
    - Watermarking
    - Batch processing
    - Video thumbnail extraction

- **Analytics**
    - Usage metrics per user
    - Processing time analytics
    - Storage usage tracking
    - Popular image formats

- **Multi-tenancy**
    - Organization support
    - Team sharing
    - Quota management
    - Billing integration

## 🎯 Success Criteria

You'll know you've succeeded when:
- ✅ You can upload an image and get processed results
- ✅ Services communicate via RabbitMQ successfully
- ✅ Jobs are tracked in PostgreSQL with proper status updates
- ✅ Images are stored in S3 and served via CloudFront
- ✅ Hybrid Cache reduces database load
- ✅ Http.Resilience handles S3 failures gracefully
- ✅ OpenTelemetry traces show end-to-end request flow
- ✅ Hangfire runs scheduled cleanup jobs
- ✅ API is protected with JWT authentication
- ✅ You can explain every piece of the architecture in an interview

## 📚 Resources

### Official Documentation
- [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/)
- [Entity Framework Core](https://learn.microsoft.com/en-us/ef/core/)
- [RabbitMQ .NET Client](https://www.rabbitmq.com/dotnet.html)
- [Hangfire](https://www.hangfire.io/)
- [AWS SDK for .NET](https://aws.amazon.com/sdk-for-net/)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/net/)

### Learning Paths
- [Microsoft Learn - .NET Aspire](https://learn.microsoft.com/en-us/training/paths/dotnet-aspire/)
- [Distributed Application Patterns](https://learn.microsoft.com/en-us/azure/architecture/patterns/)

---

## 🚦 Getting Started

Ready to build? Start with Phase 1 and work your way through. Don't try to implement everything at once - the goal is to learn each piece thoroughly.

Good luck! 🎉
