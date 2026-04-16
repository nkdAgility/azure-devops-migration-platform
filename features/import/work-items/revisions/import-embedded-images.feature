Feature: Import Embedded Image URL Rewriting
  As a migration operator
  I want embedded images in work item fields to be uploaded to the target
  So that images are visible in the target project without broken links

  Background:
    Given a valid migration package exists at the configured package root
    And a revision folder contains embedded image files

  @embedded-images
  Scenario: Embedded images are uploaded and URLs are rewritten in field values
    Given a revision's "embeddedImages" list contains an entry with "originalUrl" and "relativePath"
    And the EmbeddedImages extension is enabled
    When the import processes that revision before applying fields (Stage B)
    Then the image binary is uploaded to the target via UploadEmbeddedImageAsync
    And all occurrences of "originalUrl" in field HTML values are replaced with the returned target URL
    And the updated field values are applied to the target work item

  @embedded-images
  Scenario: Embedded image processing is skipped when extension is disabled
    Given a revision contains embedded images
    And the EmbeddedImages extension is set to disabled
    When the import processes Stage B for that revision
    Then no embedded images are uploaded
    And field values are applied to the target without URL rewriting
