sequenceDiagram
    participant User
    participant WebApp
    participant BasketAPI
    participant CatalogAPI
    participant OrderingAPI
    participant PaymentProcessor
    participant EventBus
    participant Jaeger
    participant Prometheus
    
    Note over User,Prometheus: Place Order Flow with OpenTelemetry Tracing
    
    User->>+WebApp: Add to Cart
    WebApp->>+CatalogAPI: Get Product Details
    Note right of CatalogAPI: Trace: GetProduct
    CatalogAPI-->>-WebApp: Product Details
    WebApp->>+BasketAPI: Add Item to Basket
    Note right of BasketAPI: Trace: AddItemToBasket
    BasketAPI-->>-WebApp: Updated Basket
    WebApp-->>-User: Item Added to Cart
    
    User->>+WebApp: Checkout
    WebApp->>+BasketAPI: Get Basket
    BasketAPI-->>-WebApp: Current Basket
    WebApp->>+OrderingAPI: Create Order
    
    Note right of OrderingAPI: Trace: PlaceOrder
    OrderingAPI->>OrderingAPI: Validate Order
    
    Note right of OrderingAPI: Spans include:<br/>- Create Order<br/>- Process Payment<br/>- Update Order Status<br/>(All with sensitive data masked)
    
    OrderingAPI->>+PaymentProcessor: Process Payment
    PaymentProcessor-->>-OrderingAPI: Payment Confirmation
    
    OrderingAPI->>+EventBus: Publish OrderStarted Event
    EventBus-->>-OrderingAPI: Event Published
    
    OrderingAPI-->>-WebApp: Order Created
    WebApp-->>-User: Order Confirmation
    
    Note over User,Prometheus: Continuous Telemetry Collection
    
    OrderingAPI->>Jaeger: Send Traces
    OrderingAPI->>Prometheus: Expose Metrics
    PaymentProcessor->>Jaeger: Send Traces
    PaymentProcessor->>Prometheus: Expose Metrics
    BasketAPI->>Jaeger: Send Traces
    BasketAPI->>Prometheus: Expose Metrics
    WebApp->>Jaeger: Send Traces
    WebApp->>Prometheus: Expose Metrics
