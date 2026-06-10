Current system context:

-   The system has Processes.
-   Each Process has one assigned Template.
-   The Template defines some validations applied when an applicant submits a fund request.
-   A Process has Groups.
-   Each Group belongs to only one Process.
-   Each Group has Participants.
-   A Participant may belong to one or more Groups.
-   Participants are not directly associated with Processes or Funds; their association happens through Groups.

New requirement:  
Introduce a new entity called Fund.

A Fund must include:

-   Fund name
-   Fund description
-   Regulation document, ideally uploaded as an attached PDF file

Business rules:

-   A Process must belong to one Fund.
-   A Fund may have one or more Processes.
-   A Fund cannot have Participants directly associated with it.
-   A Participant must always be registered inside at least one Group.
-   A Group must always belong to a Process.
-   A Process must always have at least one Group.
-   Since the Participant belongs to a Group, and the Group belongs to a Process, and the Process belongs to a Fund, the Participant is indirectly subject to the Fund’s regulation.

Main goal:  
Define the simplest possible functional requirement to support creating, editing, and maintaining Funds, and associating Processes with Funds. Do not overcomplicate the database design. Focus on simple product behavior, screen changes, validations, and reporting/query implications.

Please produce:

1.  Clarifying questions  
    Ask only the most important questions needed before implementation.
    
2.  Current model summary  
    Restate the existing hierarchy clearly.
    
3.  Proposed new hierarchy  
    Describe the new relationship:  
    Fund → Process → Group → Participant
    
4.  Functional requirements  
    Define requirements for:
    

-   Creating a Fund
-   Editing a Fund
-   Uploading/replacing/removing the regulation PDF
-   Associating a Process with a Fund
-   Validating that every Process belongs to a Fund
-   Ensuring Participants are only linked through Groups

5.  Screen/UI changes  
    Identify which screens likely need changes, especially:

-   Fund maintenance screen
-   Process create/edit screen
-   Participant registration screen, if needed
-   Any group or reporting screens affected

6.  Validation rules  
    List required validations in plain language.
    
7.  Reporting/query needs  
    Describe the queries that should become possible, such as:
    

-   View all Processes for a Fund
-   View all Groups under a Fund
-   View all Participants indirectly associated with a Fund through Process and Group

8.  Edge cases  
    Identify possible edge cases, such as:

-   Existing Processes without Funds
-   Deleting a Fund with Processes
-   Removing the only Group from a Process
-   Registering a Participant without a Group
-   Uploading invalid document formats

9.  Suggested implementation approach  
    Give a simple phased approach:

-   Minimal viable version
-   Follow-up improvements

10.  Acceptance criteria  
     Write clear acceptance criteria using Given / When / Then format.

Keep the output practical, simple, and implementation-ready. Avoid unnecessary database design unless it is needed to explain the requirement.