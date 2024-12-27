# Test Cases for Azure Functions

## PageCrawlerHttp Tests (Postman calls)

### RunAsync Tests

- [x] Test successful crawl with valid URLs
- [x] Test empty request body
- [x] Test invalid JSON in request body
- [x] Test request with empty URLs array
- [x] Test request with missing source
- [x] Test request with invalid URLs
- [x] Test response format for successful crawl
- [ ] Test handling of timeouts
- [ ] Test handling of large number of URLs

### Error Handling Tests

- [x] Test CreateErrorResponseAsync with different status codes
- [x] Test error messages format
- [x] Test logging of errors

## CrawlIndexRunner Tests

### ProcessSitemapAsync Tests

- [x] Test crawler running on schedule with different cron expressions
- [x] Test processing valid sitemap with URLs
- [x] Test processing valid sitemap index
- [x] Test handling empty sitemap URL
- [ ] Test handling invalid XML format
- [x] Test handling HTTP request failures
- [x] Test handling network timeout
- [ ] Test processing large sitemap (performance test)
- [ ] Test swap command messages are sent correctly (begin/end)

### Integration Tests

- [x] Test end-to-end flow with mock sitemap
- [x] Test ServiceBus message sending
- [x] Test concurrent sitemap processing

## PageCrawlerQueue Tests

### RunAsync Tests

- [x] Test processing valid trigger message
- [x] Test processing multiple messages in queue
- [x] Test queue empty scenario
- [ ] Test message abandonment on error
- [ ] Test cancellation token handling

### Integration Tests

- [x] Test end-to-end queue processing
- [x] Test ServiceBus client connection
- [x] Test message flow through the system

---

## Common Test Scenarios (Applicable to all functions)

### Error Handling

- [x] Test logging behavior
- [x] Test exception propagation
- [x] Test retry mechanisms
- [x] Test timeout handling

### Performance Tests

- [ ] Test under load
- [ ] Test memory usage
- [ ] Test response times
- [ ] Test concurrent requests

### Security Tests

- [x] Test authorization levels
- [x] Test input validation
- [ ] Test handling of malicious input

### Configuration Tests

- [x] Test different environment configurations
- [x] Test configuration changes handling
- [x] Test invalid configuration scenarios
