#include "mainwindow.h"

#include "githubclient.h"

#include <QAbstractItemView>
#include <QByteArray>
#include <QCryptographicHash>
#include <QDragEnterEvent>
#include <QDragLeaveEvent>
#include <QDropEvent>
#include <QFile>
#include <QFileDialog>
#include <QFileInfo>
#include <QFormLayout>
#include <QGroupBox>
#include <QHBoxLayout>
#include <QHeaderView>
#include <QLabel>
#include <QLineEdit>
#include <QList>
#include <QMimeData>
#include <QPlainTextEdit>
#include <QPushButton>
#include <QSettings>
#include <QShortcut>
#include <QTableWidget>
#include <QTableWidgetItem>
#include <QUrl>
#include <QVBoxLayout>
#include <QWidget>

#include <algorithm>

namespace {
enum Column { ColName = 0, ColFile = 1, ColHash = 2, ColStatus = 3, ColumnCount = 4 };
}

MainWindow::MainWindow(QWidget *parent)
    : QMainWindow(parent)
    , m_github(new GitHubClient(this))
{
    buildUi();
    setAcceptDrops(true);
    loadSettings();
    updateButtons();

    connect(m_github, &GitHubClient::log, this, &MainWindow::appendLog);
    connect(m_github, &GitHubClient::entryProcessed, this,
            [this](const QString &hash, const QString &status) {
        const int row = rowForHash(hash);
        if (row != -1)
            m_table->item(row, ColStatus)->setText(status);
        appendLog("  " + hash.left(12) + "... : " + status);
    });
    connect(m_github, &GitHubClient::finished, this, [this](bool ok, const QString &message) {
        appendLog((ok ? "SUCCESS: " : "ERROR: ") + message);
        setControlsBusy(false);
    });
}

void MainWindow::buildUi()
{
    setWindowTitle("SGLoader Patch Validator");
    resize(640, 720);

    auto *central = new QWidget(this);
    auto *root = new QVBoxLayout(central);

    // GitHub settings.
    auto *githubBox = new QGroupBox("GitHub", central);
    auto *githubForm = new QFormLayout(githubBox);

    m_tokenEdit = new QLineEdit(githubBox);
    m_tokenEdit->setEchoMode(QLineEdit::Password);
    m_tokenEdit->setPlaceholderText("Personal access token (Contents: read & write)");
    m_repoEdit = new QLineEdit(githubBox);
    m_branchEdit = new QLineEdit(githubBox);

    githubForm->addRow("Token:", m_tokenEdit);
    githubForm->addRow("Repository:", m_repoEdit);
    githubForm->addRow("Branch:", m_branchEdit);
    root->addWidget(githubBox);

    connect(m_tokenEdit, &QLineEdit::editingFinished, this, &MainWindow::saveSettings);
    connect(m_repoEdit, &QLineEdit::editingFinished, this, &MainWindow::saveSettings);
    connect(m_branchEdit, &QLineEdit::editingFinished, this, &MainWindow::saveSettings);
    connect(m_tokenEdit, &QLineEdit::textChanged, this, [this]() { updateButtons(); });

    // Drop zone.
    m_dropLabel = new QLabel("Drag patch .dll files here (multiple supported)", central);
    m_dropLabel->setObjectName("dropZone");
    m_dropLabel->setAlignment(Qt::AlignCenter);
    m_dropLabel->setMinimumHeight(64);
    setDropHighlight(false);
    root->addWidget(m_dropLabel);

    auto *listButtons = new QHBoxLayout();
    auto *browseButton = new QPushButton("Add files...", central);
    m_clearButton = new QPushButton("Clear list", central);
    connect(browseButton, &QPushButton::clicked, this, &MainWindow::browseForFiles);
    connect(m_clearButton, &QPushButton::clicked, this, &MainWindow::clearList);
    listButtons->addWidget(browseButton);
    listButtons->addWidget(m_clearButton);
    listButtons->addStretch();
    root->addLayout(listButtons);

    // Patch table.
    m_table = new QTableWidget(0, ColumnCount, central);
    m_table->setHorizontalHeaderLabels({ "Patch name", "File", "SHA-256", "Status" });
    m_table->verticalHeader()->setVisible(false);
    m_table->setSelectionBehavior(QAbstractItemView::SelectRows);
    m_table->horizontalHeader()->setSectionResizeMode(ColName, QHeaderView::Interactive);
    m_table->horizontalHeader()->setSectionResizeMode(ColFile, QHeaderView::Interactive);
    m_table->horizontalHeader()->setSectionResizeMode(ColHash, QHeaderView::Stretch);
    m_table->horizontalHeader()->setSectionResizeMode(ColStatus, QHeaderView::Interactive);
    m_table->setColumnWidth(ColName, 150);
    m_table->setColumnWidth(ColFile, 130);
    m_table->setColumnWidth(ColStatus, 170);

    // Delete key removes the selected rows (only when the table itself - not a cell
    // editor - has focus, so editing a name still uses Delete normally).
    auto *deleteShortcut = new QShortcut(QKeySequence(QKeySequence::Delete), m_table);
    deleteShortcut->setContext(Qt::WidgetShortcut);
    connect(deleteShortcut, &QShortcut::activated, this, &MainWindow::removeSelectedRows);

    root->addWidget(m_table, 2);

    // Verdict buttons.
    auto *buttons = new QHBoxLayout();
    m_approveButton = new QPushButton("Approve all", central);
    m_rejectButton = new QPushButton("Reject all", central);
    m_approveButton->setStyleSheet(
        "QPushButton { background:#4CAF50; color:white; font-weight:bold; padding:9px; }"
        "QPushButton:disabled { background:#9E9E9E; }");
    m_rejectButton->setStyleSheet(
        "QPushButton { background:#F44336; color:white; font-weight:bold; padding:9px; }"
        "QPushButton:disabled { background:#9E9E9E; }");
    buttons->addWidget(m_approveButton);
    buttons->addWidget(m_rejectButton);
    root->addLayout(buttons);

    connect(m_approveButton, &QPushButton::clicked, this, [this]() { submit(true); });
    connect(m_rejectButton, &QPushButton::clicked, this, [this]() { submit(false); });

    // Activity log.
    m_log = new QPlainTextEdit(central);
    m_log->setReadOnly(true);
    m_log->setPlaceholderText("Activity log");
    root->addWidget(m_log, 1);

    setCentralWidget(central);
}

void MainWindow::setDropHighlight(bool on)
{
    const QString colour = on ? "#4CAF50" : "#888888";
    m_dropLabel->setStyleSheet(
        QString("QLabel#dropZone { border:2px dashed %1; border-radius:8px;"
                " padding:12px; color:%1; font-size:13px; }")
            .arg(colour));
}

void MainWindow::dragEnterEvent(QDragEnterEvent *event)
{
    if (!event->mimeData()->hasUrls())
        return;

    for (const QUrl &url : event->mimeData()->urls())
    {
        if (url.isLocalFile())
        {
            event->acceptProposedAction();
            setDropHighlight(true);
            return;
        }
    }
}

void MainWindow::dragLeaveEvent(QDragLeaveEvent *event)
{
    Q_UNUSED(event);
    setDropHighlight(false);
}

void MainWindow::dropEvent(QDropEvent *event)
{
    setDropHighlight(false);
    bool handled = false;
    for (const QUrl &url : event->mimeData()->urls())
    {
        if (url.isLocalFile())
        {
            addFile(url.toLocalFile());
            handled = true;
        }
    }
    if (handled)
        event->acceptProposedAction();
}

void MainWindow::browseForFiles()
{
    const QStringList paths = QFileDialog::getOpenFileNames(
        this, "Select patches", QString(), "Patch assemblies (*.dll);;All files (*)");
    for (const QString &path : paths)
        addFile(path);
}

void MainWindow::addFile(const QString &path)
{
    QFile file(path);
    if (!file.open(QIODevice::ReadOnly))
    {
        appendLog("Cannot open file: " + path);
        return;
    }

    const QByteArray data = file.readAll();
    file.close();

    // SHA-256 of the raw file bytes, lowercase hex - matches SGLoader's PatchAssessor.
    const QString hash = QString::fromLatin1(
        QCryptographicHash::hash(data, QCryptographicHash::Sha256).toHex());

    const QFileInfo info(path);

    if (rowForHash(hash) != -1)
    {
        appendLog("Skipped (same hash already in list): " + info.fileName());
        return;
    }

    const int row = m_table->rowCount();
    m_table->insertRow(row);

    auto *nameItem = new QTableWidgetItem(info.completeBaseName());
    auto *fileItem = new QTableWidgetItem(info.fileName());
    auto *hashItem = new QTableWidgetItem(hash);
    auto *statusItem = new QTableWidgetItem(QString());

    // Only the patch name is editable.
    fileItem->setFlags(fileItem->flags() & ~Qt::ItemIsEditable);
    hashItem->setFlags(hashItem->flags() & ~Qt::ItemIsEditable);
    statusItem->setFlags(statusItem->flags() & ~Qt::ItemIsEditable);

    m_table->setItem(row, ColName, nameItem);
    m_table->setItem(row, ColFile, fileItem);
    m_table->setItem(row, ColHash, hashItem);
    m_table->setItem(row, ColStatus, statusItem);

    appendLog(QString("Added %1  (SHA-256 %2)").arg(info.fileName(), hash));
    if (info.suffix().compare("dll", Qt::CaseInsensitive) != 0)
        appendLog("Note: " + info.fileName() + " is not a .dll - hashing it anyway.");

    updateButtons();
}

void MainWindow::clearList()
{
    m_table->setRowCount(0);
    updateButtons();
}

void MainWindow::removeSelectedRows()
{
    if (m_busy)
        return;

    QList<int> rows;
    const QList<QTableWidgetItem *> items = m_table->selectedItems();
    for (QTableWidgetItem *item : items)
    {
        if (!rows.contains(item->row()))
            rows.append(item->row());
    }

    // Remove from the bottom up so the remaining indices stay valid.
    std::sort(rows.begin(), rows.end());
    for (int i = rows.size() - 1; i >= 0; --i)
        m_table->removeRow(rows[i]);

    if (!rows.isEmpty())
        appendLog(QString("Removed %1 patch(es) from the list.").arg(rows.size()));

    updateButtons();
}

int MainWindow::rowForHash(const QString &hash) const
{
    for (int row = 0; row < m_table->rowCount(); ++row)
    {
        const QTableWidgetItem *item = m_table->item(row, ColHash);
        if (item && item->text().compare(hash, Qt::CaseInsensitive) == 0)
            return row;
    }
    return -1;
}

void MainWindow::submit(bool approved)
{
    if (m_table->rowCount() == 0)
        return;

    saveSettings();
    m_github->setToken(m_tokenEdit->text().trimmed());
    m_github->setRepo(m_repoEdit->text().trimmed());
    const QString branch = m_branchEdit->text().trimmed();
    m_github->setBranch(branch.isEmpty() ? QStringLiteral("main") : branch);

    QList<GitHubClient::Entry> entries;
    for (int row = 0; row < m_table->rowCount(); ++row)
    {
        GitHubClient::Entry entry;
        entry.name = m_table->item(row, ColName)->text().trimmed();
        entry.hash = m_table->item(row, ColHash)->text().trimmed();
        if (entry.name.isEmpty())
            entry.name = m_table->item(row, ColFile)->text();
        entries.append(entry);
        m_table->item(row, ColStatus)->setText("submitting...");
    }

    setControlsBusy(true);
    appendLog(QString("Submitting %1 patch(es) as %2 ...")
                  .arg(entries.size())
                  .arg(approved ? "APPROVED" : "REJECTED"));
    m_github->submitBatch(entries, approved);
}

void MainWindow::appendLog(const QString &message)
{
    m_log->appendPlainText(message);
}

void MainWindow::updateButtons()
{
    const bool ready = !m_busy
                       && m_table->rowCount() > 0
                       && !m_tokenEdit->text().trimmed().isEmpty();
    m_approveButton->setEnabled(ready);
    m_rejectButton->setEnabled(ready);
    m_clearButton->setEnabled(!m_busy && m_table->rowCount() > 0);
}

void MainWindow::setControlsBusy(bool busy)
{
    m_busy = busy;
    m_table->setEnabled(!busy);
    updateButtons();
}

void MainWindow::loadSettings()
{
    QSettings settings;
    m_tokenEdit->setText(settings.value("github/token").toString());
    m_repoEdit->setText(settings.value("github/repo", "AZERBAIJAN-TECH/patch-validation").toString());
    m_branchEdit->setText(settings.value("github/branch", "main").toString());
}

void MainWindow::saveSettings()
{
    QSettings settings;
    settings.setValue("github/token", m_tokenEdit->text());
    settings.setValue("github/repo", m_repoEdit->text());
    settings.setValue("github/branch", m_branchEdit->text());
}
