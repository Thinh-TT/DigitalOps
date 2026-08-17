import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { AuthContext, type AuthContextValue } from "../shared/auth/auth-context";
import * as catalogService from "../shared/document-catalog/document-catalog-service";
import * as searchService from "../shared/search/document-search-service";
import type { DocumentSearchResult } from "../shared/search/types";
import { DocumentSearchPage } from "./DocumentSearchPage";

vi.mock("../shared/document-catalog/document-catalog-service");
vi.mock("../shared/search/document-search-service");

beforeEach(() => {
  vi.mocked(catalogService.getAllDocumentTypes).mockResolvedValue([
    {
      id: "type-1",
      code: "CV",
      name: "Công văn",
      description: null,
      isActive: true,
      createdAt: "2026-08-01T00:00:00Z",
      updatedAt: "2026-08-01T00:00:00Z",
    },
  ]);

  vi.mocked(searchService.searchDocuments).mockResolvedValue({
    items: [createSearchResult()],
    page: 1,
    pageSize: 20,
    totalCount: 1,
    totalPages: 1,
  });
});

describe("DocumentSearchPage (SCR-016)", () => {
  it("renders search bar and controls, validates minimum 2 characters", async () => {
    const user = userEvent.setup();
    renderSearchRoute("/search");

    expect(screen.getByText("Tìm kiếm toàn văn")).toBeInTheDocument();
    expect(
      screen.getByPlaceholderText(
        "Nhập từ khóa tìm kiếm văn bản hoặc nội dung tệp (tối thiểu 2 ký tự)...",
      ),
    ).toBeInTheDocument();

    // Click search with empty input -> validation error
    await user.click(screen.getByRole("button", { name: /Tìm kiếm/i }));
    expect(
      await screen.findByText("Từ khóa tìm kiếm phải có tối thiểu 2 ký tự."),
    ).toBeInTheDocument();
    expect(searchService.searchDocuments).not.toHaveBeenCalled();

    // Type 1 character -> still invalid
    const input = screen.getByPlaceholderText(
      "Nhập từ khóa tìm kiếm văn bản hoặc nội dung tệp (tối thiểu 2 ký tự)...",
    );
    await user.type(input, "a");
    await user.click(screen.getByRole("button", { name: /Tìm kiếm/i }));
    expect(
      await screen.findByText("Từ khóa tìm kiếm phải có tối thiểu 2 ký tự."),
    ).toBeInTheDocument();
    expect(searchService.searchDocuments).not.toHaveBeenCalled();
  });

  it("triggers search from URL query params and displays results with snippets and tags", async () => {
    renderSearchRoute("/search?q=quy%20ho%E1%BA%A1ch");

    await waitFor(() =>
      expect(searchService.searchDocuments).toHaveBeenCalledWith({
        q: "quy hoạch",
        documentKind: undefined,
        documentTypeId: undefined,
        incomingStatus: undefined,
        outgoingStatus: undefined,
        matchSource: undefined,
        dateFrom: undefined,
        dateTo: undefined,
        page: 1,
        pageSize: 20,
      }),
    );

    expect(await screen.findByText("100/UBND-QH")).toBeInTheDocument();
    expect(screen.getByText("Văn bản đến")).toBeInTheDocument();
    expect(screen.getByText("Khớp trích yếu")).toBeInTheDocument();
    expect(
      screen.getByText(/Báo cáo phương án quy hoạch đô thị/),
    ).toBeInTheDocument();
  });

  it("navigates to incoming document detail on clicking 'Mở chi tiết'", async () => {
    const user = userEvent.setup();
    renderSearchRoute("/search?q=quy%20ho%E1%BA%A1ch");

    expect(await screen.findByText("100/UBND-QH")).toBeInTheDocument();

    const detailButton = screen.getByRole("button", { name: /Mở chi tiết/i });
    await user.click(detailButton);

    expect(await screen.findByTestId("detail-page-incoming-doc-id")).toBeInTheDocument();
  });

  it("renders untrusted snippet markup as text while preserving the highlight", async () => {
    vi.mocked(searchService.searchDocuments).mockResolvedValueOnce({
      items: [createSearchResult({ snippet: '<img src=x onerror=alert(1)> <b>quy hoạch</b>' })],
      page: 1,
      pageSize: 20,
      totalCount: 1,
      totalPages: 1,
    });

    const view = renderSearchRoute("/search?q=quy%20ho%E1%BA%A1ch");

    const snippet = await waitFor(() => {
      const element = view.container.querySelector(".search-snippet");
      expect(element).not.toBeNull();
      return element as HTMLElement;
    });
    expect(snippet).toHaveTextContent("<img src=x onerror=alert(1)>");
    expect(view.container.querySelector("img")).toBeNull();
    expect(screen.getByText("quy hoạch", { selector: "strong" })).toBeInTheDocument();
  });

  it("shows empty state when no matching results are returned", async () => {
    vi.mocked(searchService.searchDocuments).mockResolvedValue({
      items: [],
      page: 1,
      pageSize: 20,
      totalCount: 0,
      totalPages: 0,
    });

    renderSearchRoute("/search?q=khongtimthay");

    expect(
      await screen.findByText(
        "Không tìm thấy văn bản phù hợp với từ khóa và bộ lọc đã chọn.",
      ),
    ).toBeInTheDocument();
  });

  it("resets filters and clears results on clicking 'Xóa bộ lọc'", async () => {
    const user = userEvent.setup();
    renderSearchRoute("/search?q=quy%20ho%E1%BA%A1ch");

    expect(await screen.findByText("100/UBND-QH")).toBeInTheDocument();

    const resetButton = screen.getByRole("button", { name: /Xóa bộ lọc/i });
    await user.click(resetButton);

    await waitFor(() => {
      expect(
        screen.getByText("Tra cứu văn bản và tệp đính kèm"),
      ).toBeInTheDocument();
    });
    expect(screen.queryByText("100/UBND-QH")).not.toBeInTheDocument();
  });
});

function renderSearchRoute(initialEntry: string) {
  const router = createMemoryRouter(
    [
      { path: "/search", element: <DocumentSearchPage /> },
      {
        path: "/incoming-documents/:id",
        element: <div data-testid="detail-page-incoming-doc-id">Incoming Detail</div>,
      },
      {
        path: "/outgoing-documents/:id",
        element: <div data-testid="detail-page-outgoing-doc-id">Outgoing Detail</div>,
      },
    ],
    { initialEntries: [initialEntry] },
  );

  return render(
    <AuthContext.Provider value={createAuthValue()}>
      <RouterProvider router={router} />
    </AuthContext.Provider>,
  );
}

function createAuthValue(): AuthContextValue {
  return {
    status: "authenticated",
    currentUser: {
      staff: {
        id: "current-staff",
        fullName: "Nguyễn Văn A",
        position: "Chuyên viên",
        department: "Văn phòng",
      },
      roles: ["Clerk"],
      mustChangePassword: false,
    },
    errorMessage: null,
    establishSession: vi.fn(),
    refreshCurrentUser: vi.fn(),
    logout: vi.fn(),
  };
}

function createSearchResult(
  overrides: Partial<DocumentSearchResult> = {},
): DocumentSearchResult {
  return {
    documentKind: "Incoming",
    documentId: "incoming-doc-id",
    referenceNumber: "100/UBND-QH",
    title: "Báo cáo phương án quy hoạch đô thị khu vực phía Tây",
    documentType: "Công văn",
    documentDate: "2026-08-10",
    matchSource: "Summary",
    snippet: "...phương án <b>quy hoạch</b> đô thị...",
    score: 0.9,
    ...overrides,
  };
}
