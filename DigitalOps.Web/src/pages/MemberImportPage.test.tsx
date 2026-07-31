import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { ApiError } from "../shared/api/api-client";
import * as memberService from "../shared/members/member-service";
import { MemberImportPage } from "./MemberImportPage";

vi.mock("../shared/members/member-service");

describe("MemberImportPage", () => {
  it("selects one XLSX file and renders a successful all-or-nothing result", async () => {
    const user = userEvent.setup();
    vi.mocked(memberService.importMembers).mockResolvedValue({
      importedCount: 2,
      totalRows: 2,
      errors: [],
    });
    const { container } = renderPage();
    const file = new File(["workbook"], "members.xlsx", {
      type: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    });

    await user.upload(getFileInput(container), file);
    expect(await screen.findByText("members.xlsx")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /Import hội viên/ }));

    await waitFor(() =>
      expect(memberService.importMembers).toHaveBeenCalledWith(file));
    expect(
      await screen.findByText("Đã import 2/2 hội viên."),
    ).toBeInTheDocument();
    expect(screen.queryByText("members.xlsx")).not.toBeInTheDocument();
  });

  it("renders all 422 row errors with Vietnamese field labels and keeps the file", async () => {
    const user = userEvent.setup();
    vi.mocked(memberService.importMembers).mockRejectedValue(new ApiError(422, {
      title: "Business validation failed",
      status: 422,
      errors: [
        { rowNumber: 1, field: "fullName", message: "Sai header." },
        {
          rowNumber: 4,
          field: "duplicateKey",
          message: "Trùng với dòng 3.",
        },
      ],
    } as never));
    const { container } = renderPage();
    const file = new File(["invalid"], "invalid.xlsx");

    await user.upload(getFileInput(container), file);
    await user.click(screen.getByRole("button", { name: /Import hội viên/ }));

    expect(
      await screen.findByText(
        "File có lỗi; hệ thống không import bất kỳ hội viên nào.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText("Họ và tên")).toBeInTheDocument();
    expect(
      screen.getByText("Họ tên + Ngày sinh + Điện thoại"),
    ).toBeInTheDocument();
    expect(screen.getByText("invalid.xlsx")).toBeInTheDocument();
  });

  it("downloads the generated template and revokes the object URL", async () => {
    const user = userEvent.setup();
    const createObjectUrl = vi.fn().mockReturnValue("blob:template");
    const revokeObjectUrl = vi.fn();
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: createObjectUrl,
    });
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: revokeObjectUrl,
    });
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(() => undefined);
    const blob = new Blob(["template"]);
    vi.mocked(memberService.downloadMemberImportTemplate).mockResolvedValue({
      blob,
      fileName: "DigitalOps-Member-Import-Template.xlsx",
    });
    renderPage();

    await user.click(screen.getByRole("button", { name: /Tải template XLSX/ }));

    await waitFor(() => expect(createObjectUrl).toHaveBeenCalledWith(blob));
    expect(click).toHaveBeenCalledTimes(1);
    expect(revokeObjectUrl).toHaveBeenCalledWith("blob:template");
  });
});

function renderPage() {
  const router = createMemoryRouter([
    { path: "/members/import", element: <MemberImportPage /> },
    { path: "/members", element: <div>member list</div> },
  ], {
    initialEntries: ["/members/import"],
  });

  return render(<RouterProvider router={router} />);
}

function getFileInput(container: HTMLElement): HTMLInputElement {
  const input = container.querySelector<HTMLInputElement>('input[type="file"]');
  if (input === null) {
    throw new Error("File input was not rendered.");
  }
  return input;
}
